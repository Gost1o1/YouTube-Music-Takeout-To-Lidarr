# Build the optional small launcher using Windows PowerShell 5.1/.NET Framework.
$ErrorActionPreference = 'Stop'
$source = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
class Program {
  [STAThread]
  static void Main() {
    try {
      string folder = AppDomain.CurrentDomain.BaseDirectory;
      string launcher = Path.Combine(folder, "Launch.cmd");
      if (!File.Exists(launcher)) throw new FileNotFoundException("Launch.cmd was not found.");
      ProcessStartInfo p = new ProcessStartInfo("cmd.exe", "/c \"\"" + launcher + "\"\"");
      p.WorkingDirectory = folder;
      p.UseShellExecute = true;
      Process.Start(p);
    } catch(Exception ex) {
      MessageBox.Show(ex.Message, "YouTube Music to Lidarr", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
  }
}
'@
$output = Join-Path $PSScriptRoot 'YouTubeMusicToLidarr.exe'
Add-Type -TypeDefinition $source -OutputAssembly $output -OutputType WindowsApplication -ReferencedAssemblies 'System.Windows.Forms'
Write-Host "Created $output"
