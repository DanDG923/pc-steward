# Capture an already-authorized child's actual native exit; never start, kill or retry it.
function Wait-OwnerOnlyDeploymentProcess {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$Process,
        [ValidateRange(1,900000)][int]$TimeoutMilliseconds = 60000
    )
    $executionPrincipal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if($executionPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
        . (Join-Path $PSScriptRoot 'OwnerOnlyExecution.ps1')
        Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $PSScriptRoot
    }
    if (!('PCSteward.Deployment.ChildExit' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace PCSteward.Deployment {
    public static class ChildExit {
        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        public static uint Wait(Process process, int milliseconds) {
            if (process==null) throw new ArgumentNullException("process");
            IntPtr handle=process.Handle;
            if (!process.WaitForExit(milliseconds)) throw new TimeoutException("Authorized deployment child is still running; do not retry or kill it.");
            uint code;
            if (!GetExitCodeProcess(handle,out code)) throw new Win32Exception(Marshal.GetLastWin32Error());
            GC.KeepAlive(process);
            return code;
        }
    }
}
'@
    }
    [PCSteward.Deployment.ChildExit]::Wait($Process,$TimeoutMilliseconds)
}
