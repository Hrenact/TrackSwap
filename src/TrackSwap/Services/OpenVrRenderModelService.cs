using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TrackSwap.Models;

namespace TrackSwap.Services
{
    public sealed class OpenVrRenderModelService
    {
        private const string RenderModelsInterfaceVersion = "FnTable:IVRRenderModels_006";
        private const int LoadingError = 100;
        private const int MaximumVertices = 65536;
        private const int MaximumTriangles = 200000;
        private const int MaximumTextureDimension = 4096;

        public OpenVrRenderModel Load(string runtimePath, string renderModelName)
        {
            if (string.IsNullOrWhiteSpace(renderModelName))
            {
                return null;
            }

            lock (OpenVrInterop.SyncRoot)
            {
                return LoadCore(runtimePath, renderModelName);
            }
        }

        private static OpenVrRenderModel LoadCore(string runtimePath, string renderModelName)
        {
            ValidateInteropLayout();
            IntPtr nativeModelPointer = IntPtr.Zero;
            IntPtr nativeTexturePointer = IntPtr.Zero;
            FreeRenderModel freeModel = null;
            FreeTexture freeTexture = null;
            try
            {
                IntPtr table = OpenVrInterop.GetInterface(runtimePath, RenderModelsInterfaceVersion);

                LoadRenderModelAsync loadModel = GetTableFunction<LoadRenderModelAsync>(table, 0);
                freeModel = GetTableFunction<FreeRenderModel>(table, 1);
                LoadTextureAsync loadTexture = GetTableFunction<LoadTextureAsync>(table, 2);
                freeTexture = GetTableFunction<FreeTexture>(table, 3);
                GetComponentCount getComponentCount = GetTableFunction<GetComponentCount>(table, 9);
                GetComponentName getComponentName = GetTableFunction<GetComponentName>(table, 10);
                GetComponentRenderModelName getComponentRenderModelName =
                    GetTableFunction<GetComponentRenderModelName>(table, 12);

                EVRRenderModelError modelError = PollUntilReady(
                    () => loadModel(renderModelName, out nativeModelPointer));
                if (modelError != EVRRenderModelError.None || nativeModelPointer == IntPtr.Zero)
                {
                    string componentModelName = FindStaticComponentModelName(
                        renderModelName,
                        getComponentCount,
                        getComponentName,
                        getComponentRenderModelName);
                    if (!string.IsNullOrWhiteSpace(componentModelName))
                    {
                        modelError = PollUntilReady(
                            () => loadModel(componentModelName, out nativeModelPointer));
                    }
                    if (modelError != EVRRenderModelError.None || nativeModelPointer == IntPtr.Zero)
                    {
                        throw new InvalidDataException(
                            "SteamVR 无法加载渲染模型 “" + renderModelName + "”，错误码：" + (int)modelError);
                    }
                }

                RenderModelNative nativeModel = Marshal.PtrToStructure<RenderModelNative>(nativeModelPointer);
                ValidateModel(nativeModel);
                OpenVrRenderVertex[] vertices = CopyVertices(nativeModel);
                ushort[] indices = CopyIndices(nativeModel);

                int textureWidth = 0;
                int textureHeight = 0;
                byte[] textureRgba = Array.Empty<byte>();
                EVRRenderModelError textureError = PollUntilReady(
                    () => loadTexture(nativeModel.DiffuseTextureId, out nativeTexturePointer));
                if (textureError == EVRRenderModelError.None && nativeTexturePointer != IntPtr.Zero)
                {
                    RenderModelTextureMapNative nativeTexture =
                        Marshal.PtrToStructure<RenderModelTextureMapNative>(nativeTexturePointer);
                    if (nativeTexture.Format == 0)
                    {
                        ValidateTexture(nativeTexture);
                        textureWidth = nativeTexture.Width;
                        textureHeight = nativeTexture.Height;
                        textureRgba = new byte[checked(textureWidth * textureHeight * 4)];
                        Marshal.Copy(nativeTexture.TextureData, textureRgba, 0, textureRgba.Length);
                    }
                }

                return new OpenVrRenderModel
                {
                    Name = renderModelName,
                    Vertices = vertices,
                    Indices = indices,
                    TextureWidth = textureWidth,
                    TextureHeight = textureHeight,
                    TextureRgba = textureRgba
                };
            }
            finally
            {
                if (nativeTexturePointer != IntPtr.Zero && freeTexture != null)
                {
                    freeTexture(nativeTexturePointer);
                }
                if (nativeModelPointer != IntPtr.Zero && freeModel != null)
                {
                    freeModel(nativeModelPointer);
                }
            }
        }

        private static EVRRenderModelError PollUntilReady(Func<EVRRenderModelError> load)
        {
            var timer = Stopwatch.StartNew();
            EVRRenderModelError error;
            do
            {
                error = load();
                if ((int)error != LoadingError)
                {
                    return error;
                }
                Thread.Sleep(10);
            }
            while (timer.Elapsed < TimeSpan.FromSeconds(5));
            return error;
        }

        private static string FindStaticComponentModelName(
            string renderModelName,
            GetComponentCount getComponentCount,
            GetComponentName getComponentName,
            GetComponentRenderModelName getComponentRenderModelName)
        {
            uint count = getComponentCount(renderModelName);
            if (count == 0 || count > 256)
            {
                return null;
            }

            var components = new System.Collections.Generic.List<Tuple<string, string>>();
            for (uint index = 0; index < count; index++)
            {
                string componentName = ReadRenderModelString((buffer, capacity) =>
                    getComponentName(renderModelName, index, buffer, capacity));
                if (string.IsNullOrWhiteSpace(componentName))
                {
                    continue;
                }
                string componentModelName = ReadRenderModelString((buffer, capacity) =>
                    getComponentRenderModelName(renderModelName, componentName, buffer, capacity));
                if (!string.IsNullOrWhiteSpace(componentModelName))
                {
                    components.Add(Tuple.Create(componentName, componentModelName));
                }
            }

            return components
                .OrderBy(component => ComponentPriority(component.Item1))
                .Select(component => component.Item2)
                .FirstOrDefault();
        }

        private static int ComponentPriority(string componentName)
        {
            if (string.Equals(componentName, "body", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
            if (string.Equals(componentName, "base", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            return 2;
        }

        private static string ReadRenderModelString(Func<StringBuilder, uint, uint> getter)
        {
            uint requiredLength = getter(null, 0);
            if (requiredLength == 0 || requiredLength > 32768)
            {
                return null;
            }
            var value = new StringBuilder((int)requiredLength);
            return getter(value, requiredLength) > 0 ? value.ToString() : null;
        }

        private static void ValidateModel(RenderModelNative model)
        {
            if (model.VertexData == IntPtr.Zero || model.IndexData == IntPtr.Zero ||
                model.VertexCount == 0 || model.VertexCount > MaximumVertices ||
                model.TriangleCount == 0 || model.TriangleCount > MaximumTriangles)
            {
                throw new InvalidDataException("SteamVR 渲染模型网格无效或过大。");
            }
        }

        private static void ValidateInteropLayout()
        {
            if (Marshal.SizeOf<RenderModelVertexNative>() != 32 ||
                Marshal.SizeOf<RenderModelNative>() != 32 ||
                Marshal.SizeOf<RenderModelTextureMapNative>() != 24)
            {
                throw new PlatformNotSupportedException("当前平台的 OpenVR 渲染模型结构布局不受支持。");
            }
        }

        private static void ValidateTexture(RenderModelTextureMapNative texture)
        {
            if (texture.TextureData == IntPtr.Zero || texture.Width == 0 || texture.Height == 0 ||
                texture.Width > MaximumTextureDimension || texture.Height > MaximumTextureDimension)
            {
                throw new InvalidDataException("SteamVR 渲染模型纹理无效或过大。");
            }
        }

        private static OpenVrRenderVertex[] CopyVertices(RenderModelNative model)
        {
            var result = new OpenVrRenderVertex[model.VertexCount];
            int size = Marshal.SizeOf<RenderModelVertexNative>();
            for (int index = 0; index < result.Length; index++)
            {
                RenderModelVertexNative vertex = Marshal.PtrToStructure<RenderModelVertexNative>(
                    IntPtr.Add(model.VertexData, checked(index * size)));
                if (!IsFinite(vertex.Position.X) || !IsFinite(vertex.Position.Y) ||
                    !IsFinite(vertex.Position.Z) || !IsFinite(vertex.Normal.X) ||
                    !IsFinite(vertex.Normal.Y) || !IsFinite(vertex.Normal.Z) ||
                    !IsFinite(vertex.TextureU) || !IsFinite(vertex.TextureV))
                {
                    throw new InvalidDataException("SteamVR 渲染模型包含无效顶点数据。");
                }
                result[index] = new OpenVrRenderVertex
                {
                    PositionX = vertex.Position.X,
                    PositionY = vertex.Position.Y,
                    PositionZ = vertex.Position.Z,
                    NormalX = vertex.Normal.X,
                    NormalY = vertex.Normal.Y,
                    NormalZ = vertex.Normal.Z,
                    TextureU = vertex.TextureU,
                    TextureV = vertex.TextureV
                };
            }
            return result;
        }

        private static ushort[] CopyIndices(RenderModelNative model)
        {
            var result = new ushort[checked(model.TriangleCount * 3)];
            for (int index = 0; index < result.Length; index++)
            {
                ushort vertexIndex = unchecked((ushort)Marshal.ReadInt16(
                    model.IndexData,
                    checked(index * sizeof(ushort))));
                if (vertexIndex >= model.VertexCount)
                {
                    throw new InvalidDataException("SteamVR 渲染模型包含越界索引。");
                }
                result[index] = vertexIndex;
            }
            return result;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static T GetTableFunction<T>(IntPtr table, int index) where T : class
        {
            IntPtr address = Marshal.ReadIntPtr(table, index * IntPtr.Size);
            if (address == IntPtr.Zero)
            {
                throw new InvalidOperationException("OpenVR RenderModels 函数表不完整。");
            }
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HmdVector3
        {
            public float X;
            public float Y;
            public float Z;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RenderModelVertexNative
        {
            public HmdVector3 Position;
            public HmdVector3 Normal;
            public float TextureU;
            public float TextureV;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RenderModelNative
        {
            public IntPtr VertexData;
            public uint VertexCount;
            public IntPtr IndexData;
            public uint TriangleCount;
            public int DiffuseTextureId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RenderModelTextureMapNative
        {
            public ushort Width;
            public ushort Height;
            public IntPtr TextureData;
            public int Format;
            public ushort MipLevels;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate EVRRenderModelError LoadRenderModelAsync(string renderModelName, out IntPtr renderModel);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FreeRenderModel(IntPtr renderModel);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate EVRRenderModelError LoadTextureAsync(int textureId, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FreeTexture(IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate uint GetComponentCount(string renderModelName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate uint GetComponentName(
            string renderModelName,
            uint componentIndex,
            StringBuilder componentName,
            uint componentNameCapacity);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private delegate uint GetComponentRenderModelName(
            string renderModelName,
            string componentName,
            StringBuilder componentRenderModelName,
            uint componentRenderModelNameCapacity);

        private enum EVRRenderModelError
        {
            None = 0
        }
    }
}
