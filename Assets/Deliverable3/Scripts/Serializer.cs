using System.IO;

public static class Serializer
{
    public static byte[] Serialize(TransformDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(dto.objectId);
        writer.Write(dto.x);
        writer.Write(dto.y);
        writer.Write(dto.z);
        writer.Write(dto.rotationY);

        return stream.ToArray();
    }

    // Same order and same types as Serialize, the bytes do not say what they are
    public static TransformDTO DeserializeTransformDTO(byte[] data)
    {
        BinaryReader reader = new BinaryReader(new MemoryStream(data));
        TransformDTO dto = new TransformDTO();

        dto.objectId = reader.ReadInt32();
        dto.x = reader.ReadSingle();
        dto.y = reader.ReadSingle();
        dto.z = reader.ReadSingle();
        dto.rotationY = reader.ReadSingle();

        return dto;
    }
}
