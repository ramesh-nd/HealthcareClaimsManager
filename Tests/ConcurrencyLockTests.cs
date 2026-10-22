using System;
using System.Threading.Tasks;
using HealthcareClaimsManager.Data;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HealthcareClaimsManager.Tests
{
    public class ConcurrencyLockTests
    {
        private readonly ReviewTaskRepository _repository;

        public ConcurrencyLockTests()
        {
            var myConfiguration = new Dictionary<string, string>
            {
                {"ConnectionStrings:DefaultConnection", "Server=localhost;Database=ClaimsQueueDB;Trusted_Connection=True;TrustServerCertificate=True;"},
                {"LockSettings:ExpirationMinutes", "15"}
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(myConfiguration!)
                .Build();

            _repository = new ReviewTaskRepository(config);
        }

        [Fact]
        public async Task TwoReviewers_RaceToLockSameTask_ExactlyOneWins_AndOneGetsConflict()
        {
            // Task 3 is open and unassigned in DUP queue
            int taskIdToRace = 3;

            Task<bool> task1 = Task.Run(() => _repository.AcquireLock(taskIdToRace, "ereyes", out _));
            Task<bool> task2 = Task.Run(() => _repository.AcquireLock(taskIdToRace, "jchen", out _));

            bool[] results = await Task.WhenAll(task1, task2);

            // Exactly one must be true, one false
            Assert.True(results[0] ^ results[1], "Exactly one reviewer must acquire the lock.");

            // Cleanup
            _repository.ReleaseLock(taskIdToRace, "ereyes");
            _repository.ReleaseLock(taskIdToRace, "jchen");
        }
    }
}