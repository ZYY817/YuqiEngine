using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GameOptimizer.Core.Constants;
using System.Windows.Controls;

namespace GameOptimizer.Shell.Views;

public partial class BenchmarkPage : UserControl
{
    private readonly List<BenchmarkRecord> _history = new();
    private bool _isRunning;

    public BenchmarkPage()
    {
        InitializeComponent();
        UpdateHistoryDisplay();
    }

    private async void RunAllTests_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        await RunAllTestsAsync();
    }

    private async void CpuTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        SetTestingState(true);
        ProgressText.Text = "正在测试CPU性能...";
        TestProgress.Value = 0;

        var (single, multi) = await Task.Run(RunCpuBenchmark);
        CpuSingleScore.Text = single.ToString("N0");
        CpuMultiScore.Text = multi.ToString("N0");

        SetTestingState(false);
    }

    private async void MemTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        SetTestingState(true);
        ProgressText.Text = "正在测试内存性能...";
        TestProgress.Value = 0;

        var (read, write) = await Task.Run(RunMemoryBenchmark);
        MemReadSpeed.Text = read.ToString("F1");
        MemWriteSpeed.Text = write.ToString("F1");

        SetTestingState(false);
    }

    private async void DiskTest_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        SetTestingState(true);
        ProgressText.Text = "正在测试磁盘性能...";
        TestProgress.Value = 0;

        var (read, write) = await Task.Run(RunDiskBenchmark);
        DiskReadSpeed.Text = read.ToString("F0");
        DiskWriteSpeed.Text = write.ToString("F0");

        SetTestingState(false);
    }

    private async Task RunAllTestsAsync()
    {
        SetTestingState(true);

        ProgressText.Text = "正在测试CPU性能...";
        TestProgress.Value = 10;
        var (cpuSingle, cpuMulti) = await Task.Run(RunCpuBenchmark);
        CpuSingleScore.Text = cpuSingle.ToString("N0");
        CpuMultiScore.Text = cpuMulti.ToString("N0");
        UpdateCpuReference(cpuSingle, cpuMulti);
        TestProgress.Value = 40;

        ProgressText.Text = "正在测试内存性能...";
        var (memRead, memWrite) = await Task.Run(RunMemoryBenchmark);
        MemReadSpeed.Text = memRead.ToString("F1");
        MemWriteSpeed.Text = memWrite.ToString("F1");
        UpdateMemReference(memRead, memWrite);
        TestProgress.Value = 70;

        ProgressText.Text = "正在测试磁盘性能...";
        var (diskRead, diskWrite) = await Task.Run(RunDiskBenchmark);
        DiskReadSpeed.Text = diskRead.ToString("F0");
        DiskWriteSpeed.Text = diskWrite.ToString("F0");
        UpdateDiskReference(diskRead, diskWrite);
        TestProgress.Value = 100;

        _history.Insert(0, new BenchmarkRecord
        {
            TestTime = DateTime.Now.ToString("MM-dd HH:mm"),
            CpuScore = $"{cpuSingle:N0}/{cpuMulti:N0}",
            MemSpeed = $"{memRead:F1}/{memWrite:F1} GB/s",
            DiskSpeed = $"{diskRead:F0}/{diskWrite:F0} MB/s"
        });

        if (_history.Count > 10) _history.RemoveAt(_history.Count - 1);
        UpdateHistoryDisplay();

        ProgressText.Text = "测试完成！";
        await Task.Delay(1500);
        SetTestingState(false);
    }

    private void SetTestingState(bool testing)
    {
        _isRunning = testing;
        ProgressPanel.Visibility = testing ? Visibility.Visible : Visibility.Collapsed;
        RunAllBtn.IsEnabled = !testing;
        CpuTestBtn.IsEnabled = !testing;
        MemTestBtn.IsEnabled = !testing;
        DiskTestBtn.IsEnabled = !testing;
    }

    private static (int single, int multi) RunCpuBenchmark()
    {
        const int iterations = 10_000_000;
        
        var sw = Stopwatch.StartNew();
        double result = 0;
        for (int i = 0; i < iterations; i++)
        {
            result += Math.Sqrt(i) * Math.Sin(i % 360) * Math.Cos(i % 360);
            result += Math.Log(i + 1) * Math.Exp(-(i % 100) / 100.0);
        }
        sw.Stop();
        var singleMs = Math.Max(sw.ElapsedMilliseconds, 1);
        var singleScore = (int)(iterations / singleMs * 10);

        var coreCount = Environment.ProcessorCount;
        sw.Restart();
        Parallel.For(0, coreCount, _ =>
        {
            double r = 0;
            for (int i = 0; i < iterations; i++)
            {
                r += Math.Sqrt(i) * Math.Sin(i % 360) * Math.Cos(i % 360);
                r += Math.Log(i + 1) * Math.Exp(-(i % 100) / 100.0);
            }
        });
        sw.Stop();
        var multiMs = Math.Max(sw.ElapsedMilliseconds, 1);
        var multiScore = (int)(iterations * coreCount / multiMs * 10);

        return (singleScore, multiScore);
    }

    private static (double read, double write) RunMemoryBenchmark()
    {
        const int size = 256 * 1024 * 1024;
        const int rounds = 3;
        var buffer = new byte[size];
        var dest = new byte[size];
        new Random().NextBytes(buffer);

        double totalReadTime = 0, totalWriteTime = 0;
        var sw = new Stopwatch();

        for (int r = 0; r < rounds; r++)
        {
            sw.Restart();
            Buffer.BlockCopy(buffer, 0, dest, 0, size);
            sw.Stop();
            totalReadTime += sw.ElapsedMilliseconds;

            sw.Restart();
            Buffer.BlockCopy(dest, 0, buffer, 0, size);
            sw.Stop();
            totalWriteTime += sw.ElapsedMilliseconds;
        }

        var avgReadMs = Math.Max(totalReadTime / rounds, 1);
        var avgWriteMs = Math.Max(totalWriteTime / rounds, 1);
        
        var readSpeed = size / 1024.0 / 1024.0 / 1024.0 / (avgReadMs / 1000.0);
        var writeSpeed = size / 1024.0 / 1024.0 / 1024.0 / (avgWriteMs / 1000.0);

        return (Math.Round(readSpeed, 1), Math.Round(writeSpeed, 1));
    }

    private static (double read, double write) RunDiskBenchmark()
    {
        Directory.CreateDirectory(AppPaths.TempDir);
        var tempPath = Path.Combine(AppPaths.TempDir, $"benchmark_{Guid.NewGuid():N}.tmp");
        const int size = 100 * 1024 * 1024;
        const int rounds = 3;
        var buffer = new byte[size];
        new Random().NextBytes(buffer);

        double totalWriteTime = 0, totalReadTime = 0;
        var sw = new Stopwatch();

        try
        {
            for (int r = 0; r < rounds; r++)
            {
                sw.Restart();
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough))
                {
                    fs.Write(buffer, 0, buffer.Length);
                    fs.Flush(true);
                }
                sw.Stop();
                totalWriteTime += sw.ElapsedMilliseconds;

                sw.Restart();
                using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
                {
                    var readBuffer = new byte[size];
                    fs.Read(readBuffer, 0, readBuffer.Length);
                }
                sw.Stop();
                totalReadTime += sw.ElapsedMilliseconds;

                File.Delete(tempPath);
            }
        }
        catch
        {
            try { File.Delete(tempPath); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Benchmark] 删除临时文件失败: {ex.Message}"); }
        }

        var avgWriteMs = Math.Max(totalWriteTime / rounds, 1);
        var avgReadMs = Math.Max(totalReadTime / rounds, 1);

        var writeSpeed = size / 1024.0 / 1024.0 / (avgWriteMs / 1000.0);
        var readSpeed = size / 1024.0 / 1024.0 / (avgReadMs / 1000.0);

        return (Math.Round(readSpeed, 0), Math.Round(writeSpeed, 0));
    }

    private void UpdateHistoryDisplay()
    {
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = _history;
        NoHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        UpdateHistoryDisplay();
    }

    // 参考基准线（基于主流硬件的平均水平）
    private const int RefCpuSingle = 80000;   // i5-12400 单核参考
    private const int RefCpuMulti = 400000;   // i5-12400 多核参考
    private const double RefMemRead = 35.0;   // DDR4-3200 读取参考
    private const double RefMemWrite = 30.0;  // DDR4-3200 写入参考
    private const double RefDiskRead = 500;   // SATA SSD 读取参考
    private const double RefDiskWrite = 450;  // SATA SSD 写入参考

    private void UpdateCpuReference(int single, int multi)
    {
        var singlePct = (single - RefCpuSingle) * 100.0 / RefCpuSingle;
        var multiPct = (multi - RefCpuMulti) * 100.0 / RefCpuMulti;
        
        CpuSingleRef.Text = singlePct >= 0 ? $"↑ 高于参考 {singlePct:F0}%" : $"↓ 低于参考 {-singlePct:F0}%";
        CpuSingleRef.Foreground = new System.Windows.Media.SolidColorBrush(
            singlePct >= 0 ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81) : System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
        
        CpuMultiRef.Text = multiPct >= 0 ? $"↑ 高于参考 {multiPct:F0}%" : $"↓ 低于参考 {-multiPct:F0}%";
        CpuMultiRef.Foreground = new System.Windows.Media.SolidColorBrush(
            multiPct >= 0 ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81) : System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
    }

    private void UpdateMemReference(double read, double write)
    {
        var readPct = (read - RefMemRead) * 100.0 / RefMemRead;
        var writePct = (write - RefMemWrite) * 100.0 / RefMemWrite;
        
        MemReadRef.Text = readPct >= 0 ? $"↑ 高于DDR4-3200参考 {readPct:F0}%" : $"↓ 低于DDR4-3200参考 {-readPct:F0}%";
        MemReadRef.Foreground = new System.Windows.Media.SolidColorBrush(
            readPct >= 0 ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81) : System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
        
        MemWriteRef.Text = writePct >= 0 ? $"↑ 高于DDR4-3200参考 {writePct:F0}%" : $"↓ 低于DDR4-3200参考 {-writePct:F0}%";
        MemWriteRef.Foreground = new System.Windows.Media.SolidColorBrush(
            writePct >= 0 ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81) : System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
    }

    private void UpdateDiskReference(double read, double write)
    {
        string readRef, writeRef;
        bool readGood, writeGood;
        
        if (read >= 2000) { readRef = "NVMe SSD 级别"; readGood = true; }
        else if (read >= 500) { readRef = "SATA SSD 级别"; readGood = true; }
        else if (read >= 150) { readRef = "HDD 级别"; readGood = false; }
        else { readRef = "低于HDD平均"; readGood = false; }
        
        if (write >= 1500) { writeRef = "NVMe SSD 级别"; writeGood = true; }
        else if (write >= 400) { writeRef = "SATA SSD 级别"; writeGood = true; }
        else if (write >= 100) { writeRef = "HDD 级别"; writeGood = false; }
        else { writeRef = "低于HDD平均"; writeGood = false; }
        
        DiskReadRef.Text = readRef;
        DiskReadRef.Foreground = new System.Windows.Media.SolidColorBrush(
            readGood ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81) : System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
        
        DiskWriteRef.Text = writeRef;
        DiskWriteRef.Foreground = new System.Windows.Media.SolidColorBrush(
            writeGood ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81) : System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
    }
}

public class BenchmarkRecord
{
    public string TestTime { get; set; } = string.Empty;
    public string CpuScore { get; set; } = string.Empty;
    public string MemSpeed { get; set; } = string.Empty;
    public string DiskSpeed { get; set; } = string.Empty;
}
