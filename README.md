# Healthcare Claims Queue Manager

## How to Run
1. Execute the `raw_data.sql` in database seed script in SQL Server.
2. Run `metrics.sql` which is attached in this project against the database to create event logging tables and metric queries.
3. Update `appsettings.json` with your SQL Server connection string.
4. Lock Duration is 15 min configurable in appsettings.json.
4. Run `dotnet run` or launch via Visual Studio / VS Code.
5. Access the application UI at `https://localhost:<Port>/Queue` or `https://localhost:<Port>/Supervisor`.

## Architectural Assumptions & Decisions
* **Sibling Task Denials**: When a claim task is denied, sibling open tasks on the same claim across other queues are set to `SystemClosed`. Completion count for metrics is attributed to the acting reviewer who performed the primary denial.
* **Expired Lock Behavior**: Expired locks are disregarded by selection algorithms, returning the task back to the available queue automatically without background cleanup jobs.
* **Forwarding Logic**: Forwarding a claim clears existing locks and reassigns the claim to any valid reviewer regardless of queue assignment, ensuring high-priority claims can be escalated across teams.
* **Pend Queue Design**: Pending a task closes the active review task with outcome `Pend` and creates a new task entry in the `PEND` queue with a 7-day extended due date.
* **Get Next on Pend Queue**: Reviewers can process the `PEND` queue like any standard queue when selecting `PEND` from the queue dropdown.
* **Active Time Definition**: Active review time is defined as the total duration from lock acquisition (`locked_on`) until resolution/closure (`closed_at`).

1. **Task Completion Attribution on Cascade Denial**
   **Decision & Reasoning**: When a reviewer's denial on a task automatically closes sibling open tasks on the same claim across other queues, only the primary task explicitly acted upon is attributed to that reviewer as a user completion. The sibling tasks are marked with status Closed and outcome SystemClosed (with closed_by_user_id = NULL), preventing artificial inflation of the reviewer's daily completion counts and avoiding skewed average review duration metrics for queues where no manual review actually took place.

2. **Work-in-Progress on Lock Expiration**
   **Decision & Reasoning**: When a lock expires, the work-in-progress remains in the queue as an open task without discarding any existing notes or state, but the reviewer's lock ownership is immediately revoked. The task becomes instantly available for any reviewer (including the original one) to acquire via Get Next or manual selection, ensuring uncompleted tasks never get permanently stuck due to idle, disconnected, or distracted reviewers.

3. **Forwarding Locks & Cross-Queue Forwarding**
   **Decision & Reasoning**: Forwarding a claim explicitly releases the sender's active lock and allows forwarding to any valid reviewer regardless of queue assignment. Releasing the lock ensures the target reviewer can immediately acquire or lock the task, while allowing cross-queue forwarding provides flexible escalation pathways when a claim requires specialized expertise from another team member.

4. **Pended Task Lifecycle, Due Date, and SLA Status**
   **Decision & Reasoning**: A pended task is marked as Closed with outcome Pend, and a new linked review task is simultaneously created in the PEND queue with a fresh due date (e.g., +7 days). Closing the original task records SLA compliance up to the point of pending, while the newly created task resets the SLA timeline to accurately measure the waiting period for external inputs (such as medical records or provider responses).

5. **Operating Get Next on the Pend Queue**
   **Decision & Reasoning**: Get Next operates on the PEND queue identically to all other queues, selecting pended tasks based on priority, due date, and explicit assignment. This ensures pended tasks are systematically picked up and reviewed as soon as their follow-up due dates arrive or when provider responses are received, avoiding manual queue searching.

## Dashboards
1. **Tasks completed per user per day**
2. **SLA Compliance (percentage of tasks completed before due date)**
3. **Time from queue entry to first review**
4. **Tasks completed in the last hour by queue**
5. **User ranking by completions per day**
6. **Average review duration per user per day (in minutes)**
7. **Arrivals vs Completions on rolling 2-hour window**
8. **Top five reviewers**
9. **Open backlog per queue**
10. **Reviewer idle time (Active time ÷ Logged-in time)**
