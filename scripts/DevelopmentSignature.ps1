# WinVerifyTrust's numeric result distinguishes an untrusted root from other
# failures that PowerShell can also group under SignatureStatus.UnknownError.
if (-not ('MindDump.Build.Authenticode' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace MindDump.Build {
    public static class Authenticode {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInfo {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr Handle;
            public IntPtr KnownSubject;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustData {
            public uint Size;
            public IntPtr PolicyCallback, SipClient;
            public uint UiChoice, RevocationChecks, UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr StateData, Url;
            public uint ProviderFlags, UiContext;
            public IntPtr SignatureSettings;
        }
        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        public static int Verify(string path) {
            var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try {
                Marshal.StructureToPtr(file, pointer, false);
                var data = new TrustData {
                    Size = (uint)Marshal.SizeOf<TrustData>(),
                    UiChoice = 2, UnionChoice = 1, File = pointer,
                    ProviderFlags = 0x1000 // Cache-only URL retrieval; no trust changes.
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }
            finally {
                Marshal.DestroyStructure<FileInfo>(pointer);
                Marshal.FreeHGlobal(pointer);
            }
        }
    }
}
'@
}

function Assert-DevelopmentSignature {
    param([string] $Path, [string] $Thumbprint, [switch] $RequireTimestamp)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $result = [MindDump.Build.Authenticode]::Verify((Get-Item -LiteralPath $Path).FullName)
    # CERT_E_UNTRUSTEDROOT only. Never accept UnknownError as a category.
    $untrustedRoot = -2146762487 # 0x800B0109
    if ($signature.SignerCertificate.Thumbprint -ne $Thumbprint -or
        $result -notin @(0, $untrustedRoot)) {
        throw ('Signature verification failed for {0}: {1}; WinVerifyTrust=0x{2:X8}' -f $Path, $signature.StatusMessage, $result)
    }
    if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) { throw "Timestamp missing: $Path" }
}
