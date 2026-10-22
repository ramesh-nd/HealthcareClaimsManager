using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using HealthcareClaimsManager.Models;

namespace HealthcareClaimsManager.Data
{
    public class ReviewTaskRepository
    {
        private readonly string _connectionString;
        private readonly int _lockExpirationMinutes;

        public ReviewTaskRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _lockExpirationMinutes = configuration.GetValue<int>("LockSettings:ExpirationMinutes", 15);
        }

        public (List<ClaimTaskViewModel> Tasks, int TotalCount) GetSupervisorQueueTasks(string queueCode, int page, int pageSize)
        {
            var tasks = new List<ClaimTaskViewModel>();
            int totalCount = 0;

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("sp_GetSupervisorQueueTasks", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@QueueCode", (object)queueCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PageNumber", page);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            conn.Open();
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                totalCount = reader.GetInt32(0);
            }

            if (reader.NextResult())
            {
                while (reader.Read())
                {
                    tasks.Add(new ClaimTaskViewModel
                    {
                        TaskId = reader.GetInt32(reader.GetOrdinal("task_id")),
                        ClaimNumber = reader.GetString(reader.GetOrdinal("claim_number")),
                        QueueCode = reader.GetString(reader.GetOrdinal("queue_code")),
                        QueueName = reader.GetString(reader.GetOrdinal("queue_name")),
                        Priority = reader.GetByte(reader.GetOrdinal("priority")),
                        DueDate = reader.GetDateTime(reader.GetOrdinal("due_date")),
                        AssignedTo = reader.IsDBNull(reader.GetOrdinal("assigned_to")) ? null : reader.GetString(reader.GetOrdinal("assigned_to")),
                        AssignedBy = reader.IsDBNull(reader.GetOrdinal("assigned_by")) ? null : reader.GetString(reader.GetOrdinal("assigned_by")),
                        LockedBy = reader.IsDBNull(reader.GetOrdinal("locked_by")) ? null : reader.GetString(reader.GetOrdinal("locked_by")),
                        LockedOn = reader.IsDBNull(reader.GetOrdinal("locked_on")) ? null : reader.GetDateTime(reader.GetOrdinal("locked_on")),
                        LockExpiresOn = reader.IsDBNull(reader.GetOrdinal("lock_expires_on")) ? null : reader.GetDateTime(reader.GetOrdinal("lock_expires_on")),
                        Note = reader.IsDBNull(reader.GetOrdinal("note")) ? null : reader.GetString(reader.GetOrdinal("note"))
                    });
                }
            }

            return (tasks, totalCount);
        }

        public ClaimTaskViewModel? GetNextClaimTask(string queueCode, string username)
        {
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("sp_GetNextClaimTask", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@QueueCode", queueCode);
            cmd.Parameters.AddWithValue("@Username", username);

            conn.Open();
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new ClaimTaskViewModel
                {
                    TaskId = reader.GetInt32(reader.GetOrdinal("task_id")),
                    ClaimNumber = reader.GetString(reader.GetOrdinal("claim_number")),
                    QueueCode = queueCode,
                    Priority = reader.GetByte(reader.GetOrdinal("priority")),
                    DueDate = reader.GetDateTime(reader.GetOrdinal("due_date")),
                    Note = reader.IsDBNull(reader.GetOrdinal("note")) ? null : reader.GetString(reader.GetOrdinal("note"))
                };
            }
            return null;
        }

        public bool AcquireLock(int taskId, string username, out string errorMessage)
        {
            errorMessage = string.Empty;
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("sp_AcquireTaskLock", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@TaskId", taskId);
            cmd.Parameters.AddWithValue("@Username", username);
            cmd.Parameters.AddWithValue("@LockDurationMinutes", _lockExpirationMinutes);

            conn.Open();
            try
            {
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (SqlException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public bool ReleaseLock(int taskId, string username)
        {
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("sp_ReleaseTaskLock", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@TaskId", taskId);
            cmd.Parameters.AddWithValue("@Username", username);

            conn.Open();
            try
            {
                cmd.ExecuteNonQuery();
                return true; // Execution succeeded without throwing an exception
            }
            catch (SqlException)
            {
                return false;
            }
        }

        public bool ForwardTask(int taskId, string currentUsername, string targetUsername)
        {
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("sp_ForwardTask", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@TaskId", taskId);
            cmd.Parameters.AddWithValue("@CurrentUsername", currentUsername);
            cmd.Parameters.AddWithValue("@TargetUsername", targetUsername);

            conn.Open();
            try
            {
                cmd.ExecuteNonQuery();
                return true; // Execution succeeded without throwing an exception
            }
            catch (SqlException)
            {
                return false;
            }
        }

        public bool CompleteReview(int taskId, string username, string outcome, string? note)
        {
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("sp_CompleteTaskReview", conn) { CommandType = CommandType.StoredProcedure };

            cmd.Parameters.AddWithValue("@TaskId", taskId);
            cmd.Parameters.AddWithValue("@Username", username);
            cmd.Parameters.AddWithValue("@Outcome", outcome);
            cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);

            conn.Open();
            try
            {
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (SqlException)
            {
                return false;
            }
        }

        public List<QueueItem> GetQueues()
        {
            var list = new List<QueueItem>();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("SELECT queue_id, queue_code, queue_name FROM dbo.queue ORDER BY queue_id", conn);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new QueueItem
                {
                    QueueId = reader.GetInt32(0),
                    QueueCode = reader.GetString(1),
                    QueueName = reader.GetString(2)
                });
            }
            return list;
        }

        public List<AppUserDto> GetReviewers()
        {
            var list = new List<AppUserDto>();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("SELECT user_id, username, display_name FROM dbo.app_user WHERE role = 'Reviewer'", conn);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AppUserDto
                {
                    UserId = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    DisplayName = reader.GetString(2)
                });
            }
            return list;
        }
    }
}