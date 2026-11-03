using UnityEngine;

namespace FFTS.Runtime
{
    /// <summary>Placeholder geometry helpers. Everything built here is labelled placeholder in ASSET_REGISTER.md.</summary>
    public static class Graybox
    {
        private static Material _base;

        public static Material MaterialFor(string hex)
        {
            if (_base == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                _base = new Material(shader);
            }
            var m = new Material(_base);
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) c = Color.gray;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, string hex, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(hex);
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Capsule(string name, Transform parent, Vector3 feet, string hex)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = feet + Vector3.up;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(hex);
            return go;
        }

        public static TextMesh Label(Transform parent, string text, Vector3 localPos, float size = 0.25f, Color? color = null)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.characterSize = size;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color ?? Color.white;
            var font = LoadBuiltinFont();
            if (font != null) { tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            go.AddComponent<Billboard>();
            return tm;
        }

        private static Font _font;
        private static Font LoadBuiltinFont()
        {
            if (_font != null) return _font;
            // Unity 2022.2+ ships LegacyRuntime.ttf; older versions ship Arial.ttf.
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { _font = null; } }
            return _font;
        }
    }

    /// <summary>Keeps world-space labels facing the camera.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
