using System;
using Unity.Assertions;
using Unity.Collections;

using GraphicsBuffer = UnityEngine.GraphicsBuffer;

namespace Unity.Rendering
{
    internal class FencedBufferPool : IDisposable
    {
        struct FrameData
        {
            public int DataBufferID;
            public int FenceBufferID;
            public UnityEngine.Rendering.AsyncGPUReadbackRequest Fence;
        }

        public int BufferSize { get; private set; }

        NativeQueue<FrameData> m_FrameData;

        BufferPool m_FenceBufferPool;
        BufferPool m_DataBufferPool;

        int m_CurrentFrameBufferID;

        public FencedBufferPool()
        {
            m_FrameData = new NativeQueue<FrameData>(Allocator.Persistent);
            m_CurrentFrameBufferID = -1;
        }

        public void Dispose()
        {
            if (m_FrameData.IsCreated)
                m_FrameData.Dispose();

            m_FenceBufferPool?.Dispose();
            m_DataBufferPool?.Dispose();

            m_CurrentFrameBufferID = -1;
        }

        public void BeginFrame()
        {
            Assert.IsTrue(m_CurrentFrameBufferID == -1);

            RecoverBuffers();
            m_CurrentFrameBufferID = m_DataBufferPool.GetBufferId();
        }

        public void EndFrame()
        {
            Assert.IsFalse(m_CurrentFrameBufferID == -1);

            var fenceBufferID = m_FenceBufferPool.GetBufferId();
            var frameData = new FrameData
            {
                DataBufferID = m_CurrentFrameBufferID,
                FenceBufferID = fenceBufferID,
            };

            if (UnityEngine.SystemInfo.supportsAsyncGPUReadback)
            {
                frameData.Fence = UnityEngine.Rendering.AsyncGPUReadback.Request(m_FenceBufferPool.GetBufferFromId(fenceBufferID));
            }

            m_FrameData.Enqueue(frameData);

            m_CurrentFrameBufferID = -1;
        }

        public GraphicsBuffer GetCurrentFrameBuffer()
        {
            Assert.IsFalse(m_CurrentFrameBufferID == -1);
            return m_DataBufferPool.GetBufferFromId(m_CurrentFrameBufferID);
        }

        // todo: improve the behavior here (GFXMESH-62).
        public void ResizeBuffer(int size, int stride)
        {
            m_FrameData.Clear();

            m_FenceBufferPool?.Dispose();
            m_DataBufferPool?.Dispose();

            m_FenceBufferPool = new BufferPool(1, 4, GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.None);
            m_DataBufferPool = new BufferPool(size, stride, GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite);

            BufferSize = size;
        }

        void RecoverBuffers()
        {
            while (CanFreeNextBuffer())
            {
                var data = m_FrameData.Dequeue();

                Assert.IsFalse(data.FenceBufferID == -1);

                m_FenceBufferPool.PutBufferId(data.FenceBufferID);
                m_DataBufferPool.PutBufferId(data.DataBufferID);
            }

            // Something is probably leaking if any of these fail.
            Assert.IsFalse(m_FrameData.Count > 15);
            Assert.IsFalse(m_DataBufferPool.TotalBufferCount > 15);
            Assert.IsFalse(m_FenceBufferPool.TotalBufferCount > 15);

            bool CanFreeNextBuffer()
            {
                // Assume 3 frames in flight if the platform does not support async readbacks.
                if (UnityEngine.SystemInfo.supportsAsyncGPUReadback)
                {
                    // Keep buffers around for another frame on Metal (GFXMESH-65).
                    // hasError is set to true when the Fence is disposed.
                    if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal)
                    {
                        return !m_FrameData.IsEmpty() && m_FrameData.Peek().Fence.hasError;
                    }
                    else
                    {
                        return !m_FrameData.IsEmpty() && m_FrameData.Peek().Fence.done;
                    }
                }
                else
                {
                    return m_FrameData.Count > 3;
                }
            }
        }
    }
}

