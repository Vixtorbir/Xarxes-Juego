using System;
using System.IO;

// Every message starts with one MsgType byte, then the content:
//   Text       01 | string                                   lobby and chat
//   Transform  02 | int id, float x, y, z, rotationY          21 bytes
//   Shoot      03 | int id, float x, y, z, dirX, dirZ         25 bytes
//   Score      04 | int round, int winner, points, deaths     lists: count, then each one
public static class Messages
{
    public static byte[] Text(string text)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writer.Write((byte)MsgType.Text);
        writer.Write(text);
        return stream.ToArray();
    }

    public static byte[] Transform(TransformDTO dto)
    {
        return AddType(MsgType.Transform, Serializer.Serialize(dto));
    }

    public static byte[] Shoot(ShootDTO dto)
    {
        return AddType(MsgType.Shoot, Serializer.Serialize(dto));
    }

    public static byte[] Score(ScoreDTO dto)
    {
        return AddType(MsgType.Score, Serializer.Serialize(dto));
    }

    static byte[] AddType(MsgType type, byte[] body)
    {
        byte[] message = new byte[1 + body.Length];
        message[0] = (byte)type;
        body.CopyTo(message, 1);
        return message;
    }

    // Fills the out that matches the type. Throws if the message is wrong, the caller drops it
    public static MsgType Read(byte[] data, out string text, out TransformDTO transform, out ShootDTO shoot, out ScoreDTO score)
    {
        text = null;
        transform = new TransformDTO();
        shoot = new ShootDTO();
        score = new ScoreDTO();
        if (data == null || data.Length < 1) throw new InvalidDataException("empty message");

        MsgType type = (MsgType)data[0];
        byte[] body = new byte[data.Length - 1];
        Array.Copy(data, 1, body, 0, body.Length);

        switch (type)
        {
            case MsgType.Text:
                BinaryReader reader = new BinaryReader(new MemoryStream(body));
                text = reader.ReadString();
                break;

            case MsgType.Transform:
                transform = Serializer.DeserializeTransformDTO(body);
                break;

            case MsgType.Shoot:
                shoot = Serializer.DeserializeShootDTO(body);
                break;

            case MsgType.Score:
                score = Serializer.DeserializeScoreDTO(body);
                break;

            default:
                throw new InvalidDataException("unknown message type " + data[0]);
        }
        return type;
    }
}
