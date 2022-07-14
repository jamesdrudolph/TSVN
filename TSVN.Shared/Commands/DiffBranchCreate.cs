using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using SamirBoulema.TSVN.Helpers;
using Task = System.Threading.Tasks.Task;

namespace SamirBoulema.TSVN.Commands
{
    [Command(PackageGuids.guidTSVNCmdSetString, PackageIds.diffBranchCreate)]
    internal sealed class DiffBranchCreateComand : BaseCommand<DiffBranchCreateComand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            var filePath = await FileHelper.GetPath();

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            await CommandHelper.DiffBranchCreate(filePath);
        }
    }
}
