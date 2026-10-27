using System;

namespace MF4CopyStudio.Models
{
    /// <summary>
    /// 复制任务进度信息 - 用于 UI 展示的文件数量估算进度
    /// </summary>
    public class CopyProgressInfo
    {
        // 文件夹统计
        public int TotalFolders { get; set; }
        public int CompletedFolders { get; set; }
        
        // 文件统计
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }  // copied + skipped + failed
        public int CopiedFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int FailedFiles { get; set; }
        
        // 字节统计
        public long TotalBytes { get; set; }
        public long ProcessedBytes { get; set; }
        public long CompletedBytes { get; set; }  // For RobocopyService
        
        // 性能指标
        public double SpeedBytesPerSecond { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        
        // 当前项目
        public string? CurrentFolderName { get; set; }
        public string? CurrentFileName { get; set; }
        
        // 进度值
        public double RawProgressValue { get; set; }      // 业务估算值 (0-100)
        public double DisplayProgressValue { get; set; }  // 平滑动画值 (0-100)
        
        // 状态
        public string Status { get; set; } = "Idle";  // Idle, Validating, Scanning, Copying, Finalizing, Completed, Cancelled, Failed
        public string Trigger { get; set; } = "Manual";  // Manual, Automatic
        
        // 特殊状态标志
        public bool IsIndeterminate { get; set; }  // 扫描阶段无法估算时使用
        public bool IsUpToDate { get; set; }       // 无新增文件
        
        // 显示文本（由 ViewModel 计算）
        public string ProgressPercentageText => $"{(int)Math.Round(DisplayProgressValue)}%";
        public string ProgressFileCountText => ProcessedFiles <= 0 && TotalFiles <= 0 
            ? "No new files" 
            : $"{ProcessedFiles} of {TotalFiles} files";
        public string ProgressCurrentItemText
        {
            get
            {
                if (string.IsNullOrEmpty(CurrentFileName))
                    return CurrentFolderName ?? "—";
                if (string.IsNullOrEmpty(CurrentFolderName))
                    return CurrentFileName;
                return $"{CurrentFolderName} · {CurrentFileName}";
            }
        }
    }
}
