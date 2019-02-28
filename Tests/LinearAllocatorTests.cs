﻿using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using AllocatorsDotNet;
using AllocatorsDotNet.Unmanaged;
using Xunit;

namespace Tests
{
    public class LinearAllocatorTests
    {
        [Fact]
        public void Disposal_CallsPinPostDispose_AssertThrowsObjDisposedEx()
        {
            var allocator = new LinearAllocator<byte>();

            allocator.Dispose();

            Assert.Throws<ObjectDisposedException>(() => allocator.Pin());
        }

        [Fact]
        public void Disposal_CallsUnpinPostDispose_AssertThrowsObjDisposedEx()
        {
            var allocator = new LinearAllocator<byte>();

            allocator.Dispose();

            Assert.Throws<ObjectDisposedException>(() => allocator.Unpin());
        }

        [Fact]
        public void Disposal_CallsGetSpanPostDispose_AssertThrowsObjDisposedEx()
        {
            var allocator = new LinearAllocator<byte>();

            allocator.Dispose();

            Assert.Throws<ObjectDisposedException>(() => allocator.GetSpan());

        }

        [Fact]
        public void Disposal_CallDisposePostDispose_Expected()
        {
            var allocator = new LinearAllocator<byte>();

            allocator.Dispose();
            allocator.Dispose();
            allocator.Dispose();
        }

        [Fact]
        public void GetSpan_WritesLegalIndices_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(1024))
            {
                Span<byte> span = allocator.GetSpan();
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = unchecked((byte)i);
                }
            }
        }

        // Due to ref struct restrictions in lambdas
        private static void InternalTestGetSpanIllegalIndices()
        {
            using (var allocator = new LinearAllocator<byte>(1024))
            {
                Span<byte> span = allocator.GetSpan();
                span[1024] = 2; // Must throw
            }
        }

        [Fact]
        public void GetSpan_WritesIllegalIndices_AssertThrowsOutOfRangeEx()
        {
            Assert.Throws<IndexOutOfRangeException>(InternalTestGetSpanIllegalIndices);
        }

        [Fact]
        public void GetSpan_ReadsWritesLegalChecksMemPreservation_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(1024))
            {
                Span<byte> span = allocator.GetSpan();

                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = unchecked((byte)i);
                }

                // ReSharper disable once RedundantAssignment
                span = default; // intentional

                Thread.Sleep(1000);
                SpinWait.SpinUntil(() => false, 1000);

                span = allocator.GetSpan();

                for (var i = 0; i < span.Length; i++)
                {
                    Assert.True(span[i] == unchecked((byte)i));
                }
            }
        }

        [Fact]
        public void Permissions_ReadsLegal_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(AllocFlags.Read, 1024))
            {
                InternalTestRead(allocator);
            }
        }

        [Fact]
        public void Permissions_WritesLegal_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(AllocFlags.Write | AllocFlags.Read, 1024))
            {
                InternalTestWrite(allocator);
            }
        }

        [Fact]
        public void Permissions_ExecutesLegal_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(AllocFlags.Execute))
            {
                InternalTestExe(allocator);
            }
        }

        [Fact]
        public void Permissions_ReadsWritesLegal_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(AllocFlags.Read | AllocFlags.Write, 1024))
            {
                InternalTestRead(allocator);
                InternalTestWrite(allocator);
            }
        }

        private Span<byte> GetReturns2MachineCode()
        {
            Architecture arch = RuntimeInformation.ProcessArchitecture;

            if (arch == Architecture.X64 || arch == Architecture.X86)
            {
                return new byte[] { 0xB8, 0x02, 0x00, 0x00, 0x00, 0xC3 }; // mov eax, 2 then ret
            }
            else
            {
                throw new NotImplementedException("TODO: Implement for ARM and ARM64");
            }
        }

        [Fact]
        public void Permissions_ReadsExecutesLegal_Expected()
        {
            Span<byte> data = GetReturns2MachineCode();

            using (LinearAllocator<byte> allocator = WriteSpanAndChangeProtection(data, AllocFlags.Read | AllocFlags.Execute))
            {
                InternalTestRead(allocator);
                InternalTestExe(allocator);
            }
        }

        [Fact]
        public void Permissions_ReadsWritesExecutesLegal_Expected()
        {
            using (var allocator = new LinearAllocator<byte>(AllocFlags.Read | AllocFlags.Write | AllocFlags.Execute, 1024))
            {
                InternalTestRead(allocator);
                InternalTestWrite(allocator);
                InternalTestExe(allocator);
            }
        }

        private static byte InternalTestRead(LinearAllocator<byte> allocator)
        {
            Span<byte> span = allocator.GetSpan();
            return unchecked((byte)(span[0] + span[500] + span[1023]));
        }

        private static void InternalTestWrite(LinearAllocator<byte> allocator)
        {
            Span<byte> span = allocator.GetSpan();
            for (var i = 0; i < span.Length; i++)
            {
                span[0] = 12;
                span[555] = 23;
                span[1023] = 255;
            }
        }

        private static LinearAllocator<byte> WriteSpanAndChangeProtection(Span<byte> sourceSpan, AllocFlags newFlags)
        {
            using (var allocator = new LinearAllocator<byte>())
            {
                Span<byte> span = allocator.GetSpan();

                sourceSpan.CopyTo(span);

                return new LinearAllocator<byte>(allocator, newFlags);
            }
        }

        private delegate int RetInt();
        private unsafe void InternalTestExe(LinearAllocator<byte> allocator)
        {
            AllocFlags flags = allocator.AllocFlags;

            var success = false;
            allocator.DangerousChangeProtection(ref success, AllocFlags.Read | AllocFlags.Write);
            Span<byte> span = allocator.GetSpan();
            GetReturns2MachineCode().CopyTo(span);
            Assert.True(success);

            success = false;
            allocator.DangerousChangeProtection(ref success, flags);
            Assert.True(success);

            RetInt del;

            fixed (byte* bPtr = span)
            {
                del = Marshal.GetDelegateForFunctionPointer<RetInt>((IntPtr)bPtr);
            }

            Assert.True(del() == 2);
        }
    }
}