#Requires -Version 5.1
# Shared interactive Lidarr discovery and paste-friendly credential dialog.
function Get-LidarrConnection {
 param([string]$Url='', [string]$ApiKey='')
 if (-not $Url) {
   $default='http://127.0.0.1:8686'
   Write-Host "Checking $default ..."
   try {
     $r=Invoke-WebRequest -Uri $default -UseBasicParsing -TimeoutSec 4 -ErrorAction Stop
     if($r.StatusCode -ge 200 -and $r.StatusCode -lt 500){ $Url=$default }
   } catch {
     # A 401/403/redirect from a reachable host still indicates a listener.
     $resp=$_.Exception.Response
     if($resp -and [int]$resp.StatusCode -ge 200 -and [int]$resp.StatusCode -lt 500){$Url=$default}
   }
   if($Url){Write-Host "Lidarr appears reachable at $Url (browser not opened)."}
   else {$Url=(Read-Host 'Lidarr URL (e.g. http://localhost:8686; Enter to cancel)').Trim()}
 }
 if(-not $Url){Write-Host 'Lidarr connection cancelled.';return $null}
 if($Url -notmatch '^https?://'){Write-Warning 'Lidarr URL must start with http:// or https://';return $null}
 if(-not $ApiKey){
   Write-Host 'In Lidarr, open the LEFT sidebar, click Settings, choose General, then find API Key.'
   Write-Host 'Use the Paste button in the next window. The key will not appear in reports.'
   try {
     Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
     Add-Type -AssemblyName System.Drawing -ErrorAction Stop
     $form=New-Object System.Windows.Forms.Form
     $form.Text='Lidarr API Key';$form.StartPosition='CenterScreen';$form.Size=New-Object System.Drawing.Size(590,215)
     $form.FormBorderStyle='FixedDialog';$form.MaximizeBox=$false;$form.MinimizeBox=$false;$form.TopMost=$true
     $label=New-Object System.Windows.Forms.Label;$label.Text='Lidarr left sidebar > Settings > General > API Key. Use Paste or Ctrl+V:'
     $label.AutoSize=$true;$label.Location=New-Object System.Drawing.Point(15,15);$form.Controls.Add($label)
     $field=New-Object System.Windows.Forms.TextBox;$field.Location=New-Object System.Drawing.Point(15,47);$field.Size=New-Object System.Drawing.Size(420,27)
     $field.UseSystemPasswordChar=$true;$form.Controls.Add($field)
     $paste=New-Object System.Windows.Forms.Button;$paste.Text='Paste';$paste.Location=New-Object System.Drawing.Point(445,44)
     $paste.Add_Click({try {if([System.Windows.Forms.Clipboard]::ContainsText()){$field.Text=[System.Windows.Forms.Clipboard]::GetText().Trim()}}catch {[System.Windows.Forms.MessageBox]::Show('Clipboard access failed. Click the box and press Ctrl+V.')|Out-Null}})
     $form.Controls.Add($paste)
     $show=New-Object System.Windows.Forms.CheckBox;$show.Text='Show key';$show.Location=New-Object System.Drawing.Point(15,87)
     $show.Add_CheckedChanged({$field.UseSystemPasswordChar=-not $show.Checked});$form.Controls.Add($show)
     $ok=New-Object System.Windows.Forms.Button;$ok.Text='Connect';$ok.Location=New-Object System.Drawing.Point(360,125);$ok.DialogResult=[System.Windows.Forms.DialogResult]::OK;$form.Controls.Add($ok)
     $cancel=New-Object System.Windows.Forms.Button;$cancel.Text='Cancel';$cancel.Location=New-Object System.Drawing.Point(455,125);$cancel.DialogResult=[System.Windows.Forms.DialogResult]::Cancel;$form.Controls.Add($cancel)
     $form.AcceptButton=$ok;$form.CancelButton=$cancel
     $form.Add_Shown({$form.Activate();$field.Focus()})
     $choice=$form.ShowDialog();if($choice -eq [System.Windows.Forms.DialogResult]::OK){$ApiKey=$field.Text.Trim()}
     $field.Clear();$form.Dispose()
   } catch {
     Write-Warning 'Windows key dialog unavailable. The following entry is visible while typing; do not share screenshots.'
     $ApiKey=(Read-Host 'Paste API key (visible)').Trim()
   }
 }
 if(-not $ApiKey){Write-Host 'API key entry cancelled.';return $null}
 return [pscustomobject]@{Url=$Url.TrimEnd('/');ApiKey=$ApiKey}
}
