namespace TrackSwap.Models
{
    public sealed class OpenVrRenderModel
    {
        public string Name { get; set; }

        public OpenVrRenderVertex[] Vertices { get; set; } = System.Array.Empty<OpenVrRenderVertex>();

        public ushort[] Indices { get; set; } = System.Array.Empty<ushort>();

        public int TextureWidth { get; set; }

        public int TextureHeight { get; set; }

        public byte[] TextureRgba { get; set; } = System.Array.Empty<byte>();
    }

    public struct OpenVrRenderVertex
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float NormalX;
        public float NormalY;
        public float NormalZ;
        public float TextureU;
        public float TextureV;
    }
}
