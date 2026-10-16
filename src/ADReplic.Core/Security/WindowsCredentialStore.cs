using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ADReplic.Core.Security
{
    /// <summary>
    /// Gestionnaire d'identifiants Windows via advapi32 (CredRead / CredWrite / CredDelete),
    /// entrées de type Generic, persistance LocalMachine : chiffrées par DPAPI pour la
    /// session Windows de l'utilisateur, non itinérantes, relisibles par tout processus
    /// de cette même session — c'est la garantie, ni plus ni moins.
    /// </summary>
    public sealed class WindowsCredentialStore : ICredentialStore
    {
        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;
        private const int ERROR_NOT_FOUND = 1168;
        private const int MaxBlobBytes = 512 * 5; // limite CredWrite pour un secret Generic

        public bool TryRead(string target, out StoredCredential credential)
        {
            credential = null;
            if (string.IsNullOrWhiteSpace(target)) return false;

            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var handle))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ERROR_NOT_FOUND) return false;
                throw new Win32Exception(error);
            }

            try
            {
                var native = Marshal.PtrToStructure<CREDENTIAL>(handle);
                var password = native.CredentialBlobSize > 0
                    ? Marshal.PtrToStringUni(native.CredentialBlob, native.CredentialBlobSize / 2)
                    : string.Empty;
                credential = new StoredCredential(native.UserName, password);
                return true;
            }
            finally
            {
                CredFree(handle);
            }
        }

        public void Save(string target, string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Cible requise.", nameof(target));
            if (string.IsNullOrEmpty(userName)) throw new ArgumentException("Compte requis.", nameof(userName));

            var blob = Encoding.Unicode.GetBytes(password ?? string.Empty);
            if (blob.Length > MaxBlobBytes) throw new ArgumentException("Mot de passe trop long pour le Gestionnaire d'identifiants.", nameof(password));

            var blobPtr = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);
                var native = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    Comment = "ADReplic - identifiants d'audit AD memorises sur ce poste",
                    CredentialBlobSize = blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = userName
                };
                if (!CredWrite(ref native, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                // Le mot de passe en clair ne reste pas en mémoire non managée.
                for (var i = 0; i < blob.Length; i++) Marshal.WriteByte(blobPtr, i, 0);
                Marshal.FreeHGlobal(blobPtr);
                Array.Clear(blob, 0, blob.Length);
            }
        }

        public bool Delete(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            if (CredDelete(target, CRED_TYPE_GENERIC, 0)) return true;

            var error = Marshal.GetLastWin32Error();
            if (error == ERROR_NOT_FOUND) return false;
            throw new Win32Exception(error);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
        private static extern bool CredWrite([In] ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);
    }
}
