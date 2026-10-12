using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Framework.Adapters
{
    // ---------------- Vector2 ----------------
    public sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter w, Vector2 v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("x"); w.WriteValue(v.x);
            w.WritePropertyName("y"); w.WriteValue(v.y);
            w.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader r, Type t, Vector2 existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Vector2((float)o["x"], (float)o["y"]);
        }
    }

    // ---------------- Vector3 ----------------
    public sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter w, Vector3 v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("x"); w.WriteValue(v.x);
            w.WritePropertyName("y"); w.WriteValue(v.y);
            w.WritePropertyName("z"); w.WriteValue(v.z);
            w.WriteEndObject();
        }

        public override Vector3 ReadJson(JsonReader r, Type t, Vector3 existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Vector3((float)o["x"], (float)o["y"], (float)o["z"]);
        }
    }

    // ---------------- Vector4 ----------------
    public sealed class Vector4Converter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter w, Vector4 v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("x"); w.WriteValue(v.x);
            w.WritePropertyName("y"); w.WriteValue(v.y);
            w.WritePropertyName("z"); w.WriteValue(v.z);
            w.WritePropertyName("w"); w.WriteValue(v.w);
            w.WriteEndObject();
        }

        public override Vector4 ReadJson(JsonReader r, Type t, Vector4 existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Vector4((float)o["x"], (float)o["y"], (float)o["z"], (float)o["w"]);
        }
    }

    // ---------------- Quaternion ----------------
    public sealed class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override void WriteJson(JsonWriter w, Quaternion q, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("x"); w.WriteValue(q.x);
            w.WritePropertyName("y"); w.WriteValue(q.y);
            w.WritePropertyName("z"); w.WriteValue(q.z);
            w.WritePropertyName("w"); w.WriteValue(q.w);
            w.WriteEndObject();
        }

        public override Quaternion ReadJson(JsonReader r, Type t, Quaternion existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Quaternion((float)o["x"], (float)o["y"], (float)o["z"], (float)o["w"]);
        }
    }

    // ---------------- Color ----------------
    public sealed class ColorConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter w, Color c, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("r"); w.WriteValue(c.r);
            w.WritePropertyName("g"); w.WriteValue(c.g);
            w.WritePropertyName("b"); w.WriteValue(c.b);
            w.WritePropertyName("a"); w.WriteValue(c.a);
            w.WriteEndObject();
        }

        public override Color ReadJson(JsonReader r, Type t, Color existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Color((float)o["r"], (float)o["g"], (float)o["b"], (float)o["a"]);
        }
    }

    // ---------------- Color32 ----------------
    public sealed class Color32Converter : JsonConverter<Color32>
    {
        public override void WriteJson(JsonWriter w, Color32 c, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("r"); w.WriteValue(c.r);
            w.WritePropertyName("g"); w.WriteValue(c.g);
            w.WritePropertyName("b"); w.WriteValue(c.b);
            w.WritePropertyName("a"); w.WriteValue(c.a);
            w.WriteEndObject();
        }

        public override Color32 ReadJson(JsonReader r, Type t, Color32 existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Color32((byte)o["r"], (byte)o["g"], (byte)o["b"], (byte)o["a"]);
        }
    }

    // ---------------- Rect ----------------
    public sealed class RectConverter : JsonConverter<Rect>
    {
        public override void WriteJson(JsonWriter w, Rect v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("x"); w.WriteValue(v.x);
            w.WritePropertyName("y"); w.WriteValue(v.y);
            w.WritePropertyName("width"); w.WriteValue(v.width);
            w.WritePropertyName("height"); w.WriteValue(v.height);
            w.WriteEndObject();
        }

        public override Rect ReadJson(JsonReader r, Type t, Rect existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            return new Rect((float)o["x"], (float)o["y"], (float)o["width"], (float)o["height"]);
        }
    }

    // ---------------- Bounds ----------------
    public sealed class BoundsConverter : JsonConverter<Bounds>
    {
        public override void WriteJson(JsonWriter w, Bounds v, JsonSerializer s)
        {
            w.WriteStartObject();
            w.WritePropertyName("center"); s.Serialize(w, v.center);
            w.WritePropertyName("size");   s.Serialize(w, v.size);
            w.WriteEndObject();
        }

        public override Bounds ReadJson(JsonReader r, Type t, Bounds existing, bool hasExisting, JsonSerializer s)
        {
            var o = JObject.Load(r);
            var center = o["center"].ToObject<Vector3>(s);
            var size   = o["size"].ToObject<Vector3>(s);
            return new Bounds(center, size);
        }
    }
}
