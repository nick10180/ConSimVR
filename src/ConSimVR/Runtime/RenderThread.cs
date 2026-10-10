using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ConSimVR
{
    /// <summary>
    /// Runs OpenXR frame submission on Unity's render thread. GL.IssuePluginEvent queues a call to a native function
    /// pointer in order with the render commands; here that pointer is an [UnmanagedCallersOnly] method, so the
    /// CoreCLR runtime BepInEx hosts handles the call directly. Code reached from here must not touch IL2CPP objects.
    /// </summary>
    internal static unsafe class RenderThread
    {
        public enum Op
        {
            BeginFrame = 1,
            EndFrame = 2,
            Fence = 3,
        }

        private const int EventTag = 0x56520000; // 'VR'
        private const int TagMask = 0x7FFF0000;

        private static int s_faults;
        private static volatile int s_fenceIssued;
        private static volatile int s_fenceReached;

        public static IntPtr CallbackPointer => (IntPtr)(delegate* unmanaged[Stdcall]<int, void>)&OnRenderEvent;

        public static int Encode(Op op, int slot) => EventTag | ((int)op << 8) | (slot & 0xFF);

        /// <summary>Queues <paramref name="op"/> for <paramref name="slot"/> after all rendering issued so far.</summary>
        public static void Issue(Op op, int slot) => GL.IssuePluginEvent(CallbackPointer, Encode(op, slot));

        /// <summary>Blocks the main thread until the render thread has processed everything queued so far.</summary>
        public static bool WaitForFence(TimeSpan timeout)
        {
            int fence = (s_fenceIssued + 1) & 0xFF;
            s_fenceIssued = fence;
            Issue(Op.Fence, fence);
            GL.Flush();
            var deadline = DateTime.UtcNow + timeout;
            while (s_fenceReached != fence)
            {
                if (DateTime.UtcNow > deadline)
                    return false;
                System.Threading.Thread.Sleep(1);
            }
            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static void OnRenderEvent(int eventId)
        {
            if ((eventId & TagMask) != EventTag)
                return;
            var op = (Op)((eventId >> 8) & 0xFF);
            if (op == Op.Fence)
            {
                s_fenceReached = eventId & 0xFF;
                return;
            }
            try
            {
                VrSession.Instance?.OnRenderThread(op, eventId & 0xFF);
            }
            catch (Exception e)
            {
                // Never let an exception unwind into native code.
                if (s_faults++ < 5)
                    Plugin.VrLog?.Error($"Render thread event failed: {e}");
            }
        }
    }
}
