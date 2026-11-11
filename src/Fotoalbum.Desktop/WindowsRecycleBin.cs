using Fotoalbum.Core;
using Microsoft.VisualBasic.FileIO;

namespace Fotoalbum.Desktop;

/// <summary>Přesun do koše Windows – fotku jde obnovit klasicky z Koše.</summary>
internal sealed class WindowsRecycleBin : IRecycleBin
{
    public string MoveToBin(string fullPath)
    {
        FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return "Koš Windows";
    }
}
