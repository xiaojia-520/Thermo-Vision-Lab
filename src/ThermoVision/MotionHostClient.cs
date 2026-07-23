using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ThermoVision
{
    internal sealed class MotionHostClient
    {
        private readonly object processLock =
            new object();

        private Process activeProcess;

        internal async Task<MotionHostResult>
            RunSoftwareZeroAsync()
        {
            string executablePath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "MotionHost",
                    "MotionHost.exe");

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    "找不到 x86 运动控制程序，请重新生成解决方案。",
                    executablePath);
            }

            Process process = new Process();
            process.StartInfo =
                new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments =
                        "--software-zero --parent-pid " +
                        Process.GetCurrentProcess().Id,
                    WorkingDirectory =
                        Path.GetDirectoryName(
                            executablePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

            lock (processLock)
            {
                if (activeProcess != null &&
                    !activeProcess.HasExited)
                {
                    process.Dispose();
                    throw new InvalidOperationException(
                        "软件零点流程正在执行，请勿重复启动。");
                }

                activeProcess = process;
            }

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "运动控制程序启动失败。");
                }

                Task<string> standardOutput =
                    process.StandardOutput.ReadToEndAsync();

                Task<string> standardError =
                    process.StandardError.ReadToEndAsync();

                await Task.Run(
                    delegate
                    {
                        process.WaitForExit();
                    });

                string output =
                    await standardOutput;

                string error =
                    await standardError;

                if (!string.IsNullOrWhiteSpace(error))
                {
                    output =
                        output +
                        Environment.NewLine +
                        error;
                }

                return new MotionHostResult(
                    process.ExitCode,
                    output.Trim());
            }
            finally
            {
                lock (processLock)
                {
                    if (ReferenceEquals(
                        activeProcess,
                        process))
                    {
                        activeProcess = null;
                    }
                }

                process.Dispose();
            }
        }
    }

    internal sealed class MotionHostResult
    {
        internal MotionHostResult(
            int exitCode,
            string output)
        {
            ExitCode = exitCode;
            Output = output;
        }

        internal int ExitCode { get; private set; }

        internal string Output { get; private set; }

        internal bool Success
        {
            get { return ExitCode == 0; }
        }
    }
}
