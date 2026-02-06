using System;

namespace FlowMaster.Domain.Models
{
    public class TestResult
    {
        public int ResultId { get; set; }
        public int DocId { get; set; }
        public string ProjectName { get; set; }
        public string Version { get; set; }
        public DateTime TestDate { get; set; }
        
        // Test Details
        public string TestCaseName { get; set; }
        public bool IsPass { get; set; }
        public string FailureReason { get; set; } // If failed
        public string Details { get; set; }
        public string BackupDbSource { get; set; } // Legacy DB Source info
    }
}
