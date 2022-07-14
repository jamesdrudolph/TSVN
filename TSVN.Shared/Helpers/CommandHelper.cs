using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using SamirBoulema.TSVN.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Process = System.Diagnostics.Process;
using Settings = SamirBoulema.TSVN.Properties.Settings;
using Task = System.Threading.Tasks.Task;

namespace SamirBoulema.TSVN.Helpers
{
    public static class CommandHelper
    {
        public static async Task Commit()
            => await RunTortoiseSvnCommand("commit");

        public static async Task Commit(string filePath)
            => await RunTortoiseSvnFileCommand("commit", filePath: filePath);

        public static async Task Revert()
            => await RunTortoiseSvnCommand("revert");

        public static async Task Revert(string filePath)
            => await RunTortoiseSvnFileCommand("revert", filePath: filePath);

        public static async Task ShowDifferences(string filePath)
            => await RunTortoiseSvnFileCommand("diff", filePath: filePath);

        public static async Task<List<string>> GetPendingChanges()
        {
            var pendingChanges = new List<string>();

            try
            {
                var repositoryRoot = await GetRepositoryRoot();

                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c cd /D \"{repositoryRoot}\" && \"{FileHelper.GetSvnExec()}\" status" + (Settings.Default.HideUnversioned ? " -q" : string.Empty),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    pendingChanges.Add(await proc.StandardOutput.ReadLineAsync());
                }
            }
            catch (Exception e)
            {
                LogHelper.Log("GetPendingChanges", e);
            }

            return pendingChanges;
        }

        public static async Task<string> GetRepositoryRoot(string path = "")
        {
            try
            {
                // Override any logic with the solution specific Root Folder setting
                var options = await OptionsHelper.GetOptions();

                if (!string.IsNullOrEmpty(options.RootFolder))
                {
                    return options.RootFolder;
                }

                // Try to find the current working folder, either by open document or by open solution
                if (string.IsNullOrEmpty(path))
                {
                    var solution = await VS.Solutions.GetCurrentSolutionAsync();
                    var documentView = await VS.Documents.GetActiveDocumentViewAsync();

                    if (!string.IsNullOrEmpty(solution?.FullPath))
                    {
                        path = Path.GetDirectoryName(solution.FullPath);
                    }
                    else if (!string.IsNullOrEmpty(documentView?.Document?.FilePath))
                    {
                        path = Path.GetDirectoryName(documentView.Document.FilePath);
                    }
                }

                // No solution or file open, we have no way of determining repository root.
                // Fail silently.
                if (string.IsNullOrEmpty(path))
                {
                    return string.Empty;
                }

                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c cd /D \"{path}\" && \"{FileHelper.GetSvnExec()}\" info --show-item wc-root",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                proc.Start();

                var errors = string.Empty;

                while (!proc.StandardError.EndOfStream)
                {
                    errors += await proc.StandardError.ReadLineAsync();
                }

                while (!proc.StandardOutput.EndOfStream)
                {
                    options.RootFolder = await proc.StandardOutput.ReadLineAsync();
                }

                await OptionsHelper.SaveOptions(options);

                if (string.IsNullOrEmpty(options.RootFolder))
                {
                    await ShowMissingSolutionDirMessage();
                }

                return options.RootFolder;
            }
            catch (Exception e)
            {
                LogHelper.Log(e);
            }

            await ShowMissingSolutionDirMessage();

            return string.Empty;
        }

        public static async Task DiffBranchCreate(string filePath)
        {
            try
            {
                var documentView = await VS.Documents.GetActiveDocumentViewAsync();

                // Try to get the current document if we weren't provided a file
                if (string.IsNullOrEmpty(filePath))
                {
                    if (!string.IsNullOrEmpty(documentView?.Document?.FilePath))
                    {
                        filePath = documentView.Document.FilePath;
                    }
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FileHelper.GetSvnExec(),
                        Arguments = $"log \"{filePath}\" -r 1:HEAD --limit 1 --stop-on-copy --xml",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                proc.Start();

                var errors = string.Empty;
                var logXml = string.Empty;

                while (!proc.StandardError.EndOfStream)
                {
                    errors += await proc.StandardError.ReadLineAsync();
                }

                if (!string.IsNullOrEmpty(errors))
                {
                    Console.Write(errors);
                }

                while (!proc.StandardOutput.EndOfStream)
                {
                    logXml += await proc.StandardOutput.ReadLineAsync();
                }

                if (string.IsNullOrEmpty(logXml))
                {
                    return;
                }

                XElement fileInfo = XElement.Parse(logXml);
                string revision = fileInfo.Element("logentry").Attribute("revision").Value;

                if (string.IsNullOrEmpty(revision))
                {
                    return;
                }

                proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FileHelper.GetSvnExec(),
                        Arguments = $"cat \"{filePath}\" -r {revision}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                proc.Start();

                errors = string.Empty;
                StringBuilder branchCreate = new StringBuilder();

                while (!proc.StandardError.EndOfStream)
                {
                    errors += await proc.StandardError.ReadLineAsync();
                }

                while (!proc.StandardOutput.EndOfStream)
                {
                    branchCreate.AppendLine(await proc.StandardOutput.ReadLineAsync());
                }

                if (branchCreate.Length <= 0)
                {
                    return;
                }

                string tempFile = Path.GetTempFileName() + ".cs";

                StreamWriter sw = new StreamWriter(tempFile, false, documentView?.Document?.Encoding);
                await sw.WriteAsync(branchCreate.ToString());
                await sw.FlushAsync();
                sw.Close();

                await VS.Commands.ExecuteAsync("Tools.DiffFiles", $"\"{filePath}\" \"{tempFile}\"");
            }
            catch (Exception e)
            {
                LogHelper.Log(e);
            }

            return;
        }

        private static async Task ShowMissingSolutionDirMessage()
        {
            await VS.MessageBox.ShowErrorAsync("Missing Working Copy Root Path",
                "Unable to determine the solution directory location. Please manually set the directory location in the settings.");
        }

        public static async Task StartProcess(string application, string args)
        {
            try
            {
                Process.Start(application, args);
            }
            catch (Exception)
            {
                await VS.MessageBox.ShowErrorAsync("TortoiseSVN not found", "TortoiseSVN not found. Did you install TortoiseSVN?");
            }
        }

        public static async Task RunTortoiseSvnCommand(string command, string args = "")
        {
            var solutionDir = await GetRepositoryRoot();

            if (string.IsNullOrEmpty(solutionDir))
            {
                return;
            }

            var tortoiseProc = FileHelper.GetTortoiseSvnProc();

            var options = await OptionsHelper.GetOptions();
            var closeOnEnd = options.CloseOnEnd ? 1 : 0;

            await StartProcess(tortoiseProc, $"/command:{command} /path:\"{solutionDir}\" {args} /closeonend:{closeOnEnd}");
        }

        public static async Task RunTortoiseSvnFileCommand(string command, string args = "", string filePath = "")
        {
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = await FileHelper.GetPath();
            }

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            var tortoiseProc = FileHelper.GetTortoiseSvnProc();

            var options = await OptionsHelper.GetOptions();
            var closeOnEnd = options.CloseOnEnd ? 1 : 0;

            await StartProcess(tortoiseProc, $"/command:{command} /path:\"{filePath}\" {args} /closeonend:{closeOnEnd}");
        }
    }
}
