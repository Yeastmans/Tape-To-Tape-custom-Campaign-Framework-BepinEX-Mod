using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace EndlessMode;

internal static class ChargedReleaseTrampoline
{
    internal static int? Correction(byte[] code, long trampoline, long original)
    {
        // Reviewed Dobby copy of the first six ReleaseShot bytes. Its widened
        // JNE must target the original RET, never the adjacent CustomShot body.
        if (code == null || code.Length != 24 || trampoline <= 0 || original <= 0
            || !code.Take(6).SequenceEqual(new byte[] { 0x80, 0x79, 0x28, 0, 0x0f, 0x85 })
            || !code.Skip(10).Take(6).SequenceEqual(new byte[] { 0xff, 0x25, 0, 0, 0, 0 })
            || BitConverter.ToInt64(code, 16) != checked(original + 6))
            throw new InvalidDataException("Unrecognized charged-release trampoline; no correction applied.");
        long expected = checked(original + 95);
        long actual = checked(trampoline + 10 + BitConverter.ToInt32(code, 6));
        if (actual == expected) return null;
        if (actual != expected + 4)
            throw new InvalidDataException("Unexpected charged-release branch destination; no correction applied.");
        return checked((int)(expected - (trampoline + 10)));
    }

    internal static bool Apply(IntPtr trampoline, IntPtr original)
    {
        byte[] before = Read(trampoline);
        int? correction = Correction(before, trampoline.ToInt64(), original.ToInt64());
        if (!correction.HasValue) return false;
        IntPtr address = trampoline + 6;
        if (!VirtualProtect(address, (UIntPtr)4, 0x40, out uint previous)) throw new System.ComponentModel.Win32Exception();
        try
        {
            if (Marshal.ReadInt32(address) != BitConverter.ToInt32(before, 6))
                throw new InvalidDataException("Charged-release trampoline changed before correction.");
            Marshal.WriteInt32(address, correction.Value);
            if (!FlushInstructionCache(new IntPtr(-1), address, (UIntPtr)4)) throw new System.ComponentModel.Win32Exception();
        }
        finally { if (!VirtualProtect(address, (UIntPtr)4, previous, out _)) throw new System.ComponentModel.Win32Exception(); }
        byte[] after = Read(trampoline);
        if (!before.Take(6).SequenceEqual(after.Take(6)) || !before.Skip(10).SequenceEqual(after.Skip(10))
            || Correction(after, trampoline.ToInt64(), original.ToInt64()).HasValue)
            throw new InvalidDataException("Charged-release trampoline verification failed after correction.");
        return true;
    }
    private static byte[] Read(IntPtr pointer) { var result = new byte[24]; Marshal.Copy(pointer, result, 0, result.Length); return result; }
    [DllImport("kernel32", SetLastError = true)] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protection, out uint previous);
    [DllImport("kernel32", SetLastError = true)] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

    internal static bool InstallChecked(Action preflight, Action install, Action validate, Action rollback, Action<Exception> report)
    {
        bool attempted = false;
        try { preflight(); attempted = true; install(); validate(); return true; }
        catch (Exception error)
        {
            // Even Patch() can fail after installing native state. Never leave
            // our potentially unsafe prefix installed after a refused repair.
            if (attempted)
            {
                try { rollback(); }
                catch (Exception rollbackError) { throw new AggregateException("Could not remove the charged-release hook after failed validation.", error, rollbackError); }
            }
            report(error); return false;
        }
    }

    internal static void RemoveOwned(Action remove, Func<bool> ownedRemains, Func<bool> otherOwnersRemain, Action detachUnowned, Action verifyNative)
    {
        try { remove(); }
        finally
        {
            if (ownedRemains()) throw new InvalidOperationException("Charged-release prefix removal did not complete.");
            if (!otherOwnersRemain()) { detachUnowned(); verifyNative(); }
        }
    }
}
