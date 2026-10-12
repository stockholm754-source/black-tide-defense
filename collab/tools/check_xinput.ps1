param([string]$OutputPath)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BtXInputEvidence {
 [StructLayout(LayoutKind.Sequential)] public struct Pad { public ushort buttons; public byte lt,rt; public short lx,ly,rx,ry; }
 [StructLayout(LayoutKind.Sequential)] public struct State { public uint packet; public Pad pad; }
 [StructLayout(LayoutKind.Sequential)] public struct Vibration { public ushort left,right; }
 [DllImport("xinput1_4.dll",EntryPoint="XInputGetState")] public static extern uint GetState(uint slot,out State state);
 [DllImport("xinput1_4.dll",EntryPoint="XInputSetState")] public static extern uint SetState(uint slot,ref Vibration vibration);
}
'@
$taskSlots=@()
for($i=0;$i -lt 4;$i++) {
 $taskState=New-Object BtXInputEvidence+State
 $taskGet=[BtXInputEvidence]::GetState([uint32]$i,[ref]$taskState)
 $taskSet=$null
 # Only probe SetState on disconnected slots; do not affect another application's controller.
 if($taskGet -eq 1167) {
  $taskVibration=New-Object BtXInputEvidence+Vibration
  $taskSet=[BtXInputEvidence]::SetState([uint32]$i,[ref]$taskVibration)
 }
 $taskSlots+=@{slot=$i;getState=$taskGet;setZeroOnDisconnectedSlot=$taskSet;connected=($taskGet -eq 0)}
}
$taskEvidence=@{time=(Get-Date -Format o);stateBytes=[System.Runtime.InteropServices.Marshal]::SizeOf([type][BtXInputEvidence+State]);vibrationBytes=[System.Runtime.InteropServices.Marshal]::SizeOf([type][BtXInputEvidence+Vibration]);slots=$taskSlots;physicalFeelTested=$false}
$taskJson=$taskEvidence | ConvertTo-Json -Depth 5
if($OutputPath) { [System.IO.File]::WriteAllText($OutputPath,$taskJson,(New-Object System.Text.UTF8Encoding($false))) }
Write-Output $taskJson
