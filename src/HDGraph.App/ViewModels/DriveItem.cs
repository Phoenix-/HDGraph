using HDGraph.Core;

namespace HDGraph.App.ViewModels;

/// <summary>A drive button on the toolbar.</summary>
public sealed record DriveItem(string RootPath, string Label, string Tooltip)
{
    public static IEnumerable<DriveItem> Enumerate()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;

            string volumeLabel;
            long free, total;
            try
            {
                if (!drive.IsReady) continue;
                volumeLabel = drive.VolumeLabel;
                free = drive.AvailableFreeSpace;
                total = drive.TotalSize;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var label = string.IsNullOrWhiteSpace(volumeLabel) ? letter : $"{letter} {volumeLabel}";
            var tooltip = $"{SizeFormatter.Format(free)} free of {SizeFormatter.Format(total)}";
            yield return new DriveItem(drive.Name, label, tooltip);
        }
    }
}
