-- Additional table for metrics and event tracking
IF OBJECT_ID('dbo.task_event_log', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_event_log (
        event_id          INT IDENTITY(1,1) PRIMARY KEY,
        task_id           INT NOT NULL REFERENCES dbo.review_task(task_id),
        user_id           INT NOT NULL REFERENCES dbo.app_user(user_id),
        event_type        VARCHAR(30) NOT NULL, -- 'FIRST_VIEW', 'LOCKED', 'RELEASED', 'FORWARDED', 'COMPLETED'
        created_at        DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX ix_event_log_task_type ON dbo.task_event_log(task_id, event_type);
END;
GO

-- REQUIRED METRIC 1: Tasks completed per user per day
SELECT 
    u.username,
    u.display_name,
    CAST(t.closed_at AS DATE) AS completion_date,
    COUNT(t.task_id) AS completed_tasks_count
FROM dbo.review_task t
JOIN dbo.app_user u ON t.closed_by_user_id = u.user_id
WHERE t.status = 'Closed'
GROUP BY u.username, u.display_name, CAST(t.closed_at AS DATE)
ORDER BY completion_date DESC, completed_tasks_count DESC;

-- REQUIRED METRIC 2: SLA Compliance (percentage of tasks completed before due date)
SELECT 
    COUNT(*) AS total_completed_tasks,
    SUM(CASE WHEN t.closed_at <= t.due_date THEN 1 ELSE 0 END) AS met_sla_count,
    ROUND(
        (CAST(SUM(CASE WHEN t.closed_at <= t.due_date THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*)) * 100, 
        2
    ) AS sla_compliance_percentage
FROM dbo.review_task t
WHERE t.status = 'Closed';

-- REQUIRED METRIC 3: Time from queue entry to first review (Median and 90th percentile in minutes)
WITH FirstReviews AS (
    SELECT 
        e.task_id,
        DATEDIFF(MINUTE, t.created_at, MIN(e.created_at)) AS duration_minutes
    FROM dbo.task_event_log e
    JOIN dbo.review_task t ON e.task_id = t.task_id
    WHERE e.event_type = 'FIRST_VIEW'
    GROUP BY e.task_id, t.created_at
)
SELECT DISTINCT
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY duration_minutes) OVER() AS median_minutes,
    PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY duration_minutes) OVER() AS p90_minutes
FROM FirstReviews;

-- OPTIONAL SUPPORTED METRICS --

-- 1. Tasks completed in the last hour by queue
SELECT q.queue_code, COUNT(t.task_id) AS completed_last_hour
FROM dbo.review_task t
JOIN dbo.queue q ON t.queue_id = q.queue_id
WHERE t.status = 'Closed' AND t.closed_at >= DATEADD(HOUR, -1, SYSUTCDATETIME())
GROUP BY q.queue_code;

-- 2. User ranking by completions per day
SELECT u.username, CAST(t.closed_at AS DATE) AS work_date, COUNT(t.task_id) AS total_completed,
       DENSE_RANK() OVER (PARTITION BY CAST(t.closed_at AS DATE) ORDER BY COUNT(t.task_id) DESC) AS rank
FROM dbo.review_task t
JOIN dbo.app_user u ON t.closed_by_user_id = u.user_id
WHERE t.status = 'Closed'
GROUP BY u.username, CAST(t.closed_at AS DATE);

-- 3. Average review duration per user per day (in minutes)
SELECT u.username, CAST(t.closed_at AS DATE) AS work_date,
       AVG(DATEDIFF(MINUTE, t.locked_on, t.closed_at)) AS avg_review_minutes
FROM dbo.review_task t
JOIN dbo.app_user u ON t.closed_by_user_id = u.user_id
WHERE t.status = 'Closed' AND t.locked_on IS NOT NULL
GROUP BY u.username, CAST(t.closed_at AS DATE);

-- 4 & 5. Arrivals vs Completions on rolling 2-hour window
SELECT 
    DATEADD(HOUR, DATEDIFF(HOUR, 0, SYSUTCDATETIME()), 0) AS window_start,
    (SELECT COUNT(*) FROM dbo.review_task WHERE created_at >= DATEADD(HOUR, -2, SYSUTCDATETIME())) AS arrivals_last_2h,
    (SELECT COUNT(*) FROM dbo.review_task WHERE closed_at >= DATEADD(HOUR, -2, SYSUTCDATETIME())) AS completions_last_2h;

-- 6. Top five reviewers
SELECT TOP 5 u.username, u.display_name, COUNT(t.task_id) AS total_completions
FROM dbo.review_task t
JOIN dbo.app_user u ON t.closed_by_user_id = u.user_id
WHERE t.status = 'Closed'
GROUP BY u.username, u.display_name
ORDER BY total_completions DESC;

-- 7. Open backlog per queue
SELECT q.queue_code, q.queue_name, COUNT(t.task_id) AS open_backlog
FROM dbo.queue q
LEFT JOIN dbo.review_task t ON q.queue_id = t.queue_id AND t.status = 'Open'
GROUP BY q.queue_code, q.queue_name;

-- 8. Reviewer idle time (Active time ÷ Logged-in time)
WITH ActiveTime AS (
    SELECT closed_by_user_id AS user_id, SUM(DATEDIFF(MINUTE, locked_on, closed_at)) AS active_minutes
    FROM dbo.review_task WHERE status = 'Closed' AND locked_on IS NOT NULL
    GROUP BY closed_by_user_id
),
LoggedInTime AS (
    SELECT user_id, SUM(DATEDIFF(MINUTE, started_at, ISNULL(ended_at, SYSUTCDATETIME()))) AS total_logged_in_minutes
    FROM dbo.user_session GROUP BY user_id
)
SELECT u.username, 
       ISNULL(a.active_minutes, 0) AS active_minutes,
       ISNULL(l.total_logged_in_minutes, 0) AS total_minutes,
       CASE WHEN ISNULL(l.total_logged_in_minutes, 0) = 0 THEN 0
            ELSE ROUND(1.0 - (CAST(ISNULL(a.active_minutes, 0) AS FLOAT) / l.total_logged_in_minutes), 2)
       END AS idle_ratio
FROM dbo.app_user u
LEFT JOIN ActiveTime a ON u.user_id = a.user_id
LEFT JOIN LoggedInTime l ON u.user_id = l.user_id
WHERE u.role = 'Reviewer';
GO
------------------------------------
 -- 1. Supervisor List View SP (Paged & Optimized)
 CREATE OR ALTER PROCEDURE dbo.sp_GetSupervisorQueueTasks
     @QueueCode VARCHAR(20) = NULL,
     @PageNumber INT = 1,
     @PageSize INT = 50
 AS
 BEGIN
     SET NOCOUNT ON;

     SELECT COUNT(1)
     FROM dbo.review_task t
     INNER JOIN dbo.queue q ON t.queue_id = q.queue_id
     WHERE t.status = 'Open' 
       AND (@QueueCode IS NULL OR q.queue_code = @QueueCode);

     SELECT 
         t.task_id,
         c.claim_number,
         q.queue_code,
         q.queue_name,
         t.priority,
         t.due_date,
         u_to.username AS assigned_to,
         u_by.username AS assigned_by,
         u_lock.username AS locked_by,
         t.locked_on,
         t.lock_expires_on,
         t.note
     FROM dbo.review_task t WITH (NOLOCK)
     INNER JOIN dbo.claim c ON t.claim_id = c.claim_id
     INNER JOIN dbo.queue q ON t.queue_id = q.queue_id
     LEFT JOIN dbo.app_user u_to ON t.assigned_to_user_id = u_to.user_id
     LEFT JOIN dbo.app_user u_by ON t.assigned_by_user_id = u_by.user_id
     LEFT JOIN dbo.app_user u_lock ON t.locked_by_user_id = u_lock.user_id
     WHERE t.status = 'Open'
       AND (@QueueCode IS NULL OR q.queue_code = @QueueCode)
     ORDER BY t.priority ASC, t.due_date ASC, c.claim_number ASC
     OFFSET (@PageNumber - 1) * @PageSize ROWS
     FETCH NEXT @PageSize ROWS ONLY;
 END;
 GO

 -- 2. Get Next Claim Task SP
 CREATE OR ALTER PROCEDURE dbo.sp_GetNextClaimTask
     @QueueCode VARCHAR(20),
     @Username VARCHAR(50)
 AS
 BEGIN
     SET NOCOUNT ON;

     DECLARE @UserId INT;
     SELECT @UserId = user_id FROM dbo.app_user WHERE username = @Username;

     DECLARE @Now DATETIME2(0) = SYSUTCDATETIME();

     SELECT TOP 1
         t.task_id,
         c.claim_number,
         t.priority,
         t.due_date,
         t.note
     FROM dbo.review_task t WITH (NOLOCK)
     INNER JOIN dbo.claim c ON t.claim_id = c.claim_id
     INNER JOIN dbo.queue q ON t.queue_id = q.queue_id
     WHERE q.queue_code = @QueueCode
       AND t.status = 'Open'
       AND (t.locked_by_user_id IS NULL OR t.lock_expires_on < @Now)
     ORDER BY 
         CASE WHEN t.assigned_to_user_id = @UserId THEN 0 ELSE 1 END ASC,
         t.priority ASC,
         t.due_date ASC,
         c.claim_number ASC;
 END;
 GO

 -- 3. Atomic Lock Acquisition SP
 CREATE OR ALTER PROCEDURE dbo.sp_AcquireTaskLock
     @TaskId INT,
     @Username VARCHAR(50),
     @LockDurationMinutes INT = 15
 AS
 BEGIN
     SET NOCOUNT ON;
     SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

     BEGIN TRANSACTION;

     DECLARE @UserId INT;
     SELECT @UserId = user_id FROM dbo.app_user WHERE username = @Username;

     IF @UserId IS NULL
     BEGIN
         RAISERROR('User not found.', 16, 1);
         ROLLBACK TRANSACTION;
         RETURN;
     END

     DECLARE @Now DATETIME2(0) = SYSUTCDATETIME();
     DECLARE @LockExpires DATETIME2(0) = DATEADD(MINUTE, @LockDurationMinutes, @Now);

     -- Atomic conditional lock update using UPDLOCK & READPAST
     UPDATE dbo.review_task WITH (UPDLOCK, ROWLOCK)
     SET locked_by_user_id = @UserId,
         locked_on = @Now,
         lock_expires_on = @LockExpires
     WHERE task_id = @TaskId
       AND status = 'Open'
       AND (
             locked_by_user_id IS NULL 
             OR lock_expires_on < @Now 
             OR locked_by_user_id = @UserId
           );

     IF @@ROWCOUNT = 0
     BEGIN
         RAISERROR('Task is currently locked by another reviewer or closed.', 16, 2);
         ROLLBACK TRANSACTION;
         RETURN;
     END

     -- Log First Review Event if not previously logged
     IF NOT EXISTS (SELECT 1 FROM dbo.task_event_log WHERE task_id = @TaskId AND event_type = 'FIRST_VIEW')
     BEGIN
         INSERT INTO dbo.task_event_log (task_id, user_id, event_type, created_at)
         VALUES (@TaskId, @UserId, 'FIRST_VIEW', @Now);
     END

     INSERT INTO dbo.task_event_log (task_id, user_id, event_type, created_at)
     VALUES (@TaskId, @UserId, 'LOCKED', @Now);

     COMMIT TRANSACTION;
 END;
 GO

 -- 4. Release Task Lock SP
 CREATE OR ALTER PROCEDURE dbo.sp_ReleaseTaskLock
     @TaskId INT,
     @Username VARCHAR(50)
 AS
 BEGIN
     SET NOCOUNT ON;

     DECLARE @UserId INT;
     SELECT @UserId = user_id FROM dbo.app_user WHERE username = @Username;

     UPDATE dbo.review_task
     SET locked_by_user_id = NULL,
         locked_on = NULL,
         lock_expires_on = NULL
     WHERE task_id = @TaskId AND locked_by_user_id = @UserId;

     IF @@ROWCOUNT > 0
     BEGIN
         INSERT INTO dbo.task_event_log (task_id, user_id, event_type, created_at)
         VALUES (@TaskId, @UserId, 'RELEASED', SYSUTCDATETIME());
     END
 END;
 GO

 -- 5. Forward Task SP
 CREATE OR ALTER PROCEDURE dbo.sp_ForwardTask
     @TaskId INT,
     @CurrentUsername VARCHAR(50),
     @TargetUsername VARCHAR(50)
 AS
 BEGIN
     SET NOCOUNT ON;

     DECLARE @CurrentUserId INT, @TargetUserId INT;
     SELECT @CurrentUserId = user_id FROM dbo.app_user WHERE username = @CurrentUsername;
     SELECT @TargetUserId = user_id FROM dbo.app_user WHERE username = @TargetUsername;

     UPDATE dbo.review_task
     SET assigned_to_user_id = @TargetUserId,
         assigned_by_user_id = @CurrentUserId,
         locked_by_user_id = NULL,
         locked_on = NULL,
         lock_expires_on = NULL
     WHERE task_id = @TaskId;

     IF @@ROWCOUNT > 0
     BEGIN
         INSERT INTO dbo.task_event_log (task_id, user_id, event_type, created_at)
         VALUES (@TaskId, @CurrentUserId, 'FORWARDED', SYSUTCDATETIME());
     END
 END;
 GO

 -- 6. Complete Task Review SP
 CREATE OR ALTER PROCEDURE dbo.sp_CompleteTaskReview
     @TaskId INT,
     @Username VARCHAR(50),
     @Outcome VARCHAR(30),
     @Note NVARCHAR(500) = NULL
 AS
 BEGIN
     SET NOCOUNT ON;
     BEGIN TRANSACTION;

     DECLARE @UserId INT, @ClaimId INT;
     SELECT @UserId = user_id FROM dbo.app_user WHERE username = @Username;

     SELECT @ClaimId = claim_id FROM dbo.review_task WHERE task_id = @TaskId;

     DECLARE @Now DATETIME2(0) = SYSUTCDATETIME();

     -- Close the target task
     UPDATE dbo.review_task
     SET status = 'Closed',
         outcome = @Outcome,
         note = ISNULL(@Note, note),
         closed_at = @Now,
         closed_by_user_id = @UserId,
         locked_by_user_id = NULL,
         locked_on = NULL,
         lock_expires_on = NULL
     WHERE task_id = @TaskId;

     -- Cascade Deny: If outcome is Deny, close open sibling tasks in other queues
     IF @Outcome = 'Deny'
     BEGIN
         UPDATE dbo.review_task
         SET status = 'Closed',
             outcome = 'SystemClosed',
             closed_at = @Now,
             note = 'Closed automatically due to claim denial on task ' + CAST(@TaskId AS VARCHAR(10)),
             locked_by_user_id = NULL,
             locked_on = NULL,
             lock_expires_on = NULL
         WHERE claim_id = @ClaimId AND task_id <> @TaskId AND status = 'Open';
     END     
     Else IF @Outcome = 'Pend' -- Pend Queue Handling: If Outcome is Pend, create a new pended task entry
     BEGIN
         DECLARE @PendQueueId INT;
         SELECT @PendQueueId = queue_id FROM dbo.queue WHERE queue_code = 'PEND';

         INSERT INTO dbo.review_task (claim_id, queue_id, priority, due_date, assigned_to_user_id, assigned_by_user_id, status, created_at, note)
         VALUES (@ClaimId, @PendQueueId, 3, DATEADD(DAY, 7, @Now), @UserId, @UserId, 'Open', @Now, @Note);
     END

     INSERT INTO dbo.task_event_log (task_id, user_id, event_type, created_at)
     VALUES (@TaskId, @UserId, 'COMPLETED', @Now);

     COMMIT TRANSACTION;
 END;
 GO