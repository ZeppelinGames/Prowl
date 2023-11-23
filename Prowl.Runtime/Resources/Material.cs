﻿using Prowl.Runtime.Components;
using Prowl.Runtime.Utils;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Prowl.Runtime.Resources
{
    public class MaterialPropertyBlock
    {
        [SerializeField] private Dictionary<string, Color> colors = new();
        [SerializeField] private Dictionary<string, Vector2> vectors2 = new();
        [SerializeField] private Dictionary<string, Vector3> vectors3 = new();
        [SerializeField] private Dictionary<string, Vector4> vectors4 = new();
        [SerializeField] private Dictionary<string, float> floats = new();
        [SerializeField] private Dictionary<string, int> ints = new();
        [SerializeField] private Dictionary<string, Matrix4x4> matrices = new();
        [SerializeField] private Dictionary<string, AssetRef<Texture2D>> textures = new();

        private Dictionary<string, int> cachedUniformLocs = new();

        //private Dictionary<string, int> textureSlots = new();

        public MaterialPropertyBlock() { }

        public MaterialPropertyBlock(MaterialPropertyBlock clone)
        {
            colors = new(clone.colors);
            vectors2 = new(clone.vectors2);
            vectors3 = new(clone.vectors3);
            vectors4 = new(clone.vectors4);
            floats = new(clone.floats);
            ints = new(clone.ints);
            matrices = new(clone.matrices);
            textures = new(clone.textures);
        }

        public bool isEmpty => colors.Count == 0 && vectors4.Count == 0 && vectors3.Count == 0 && vectors2.Count == 0 && floats.Count == 0 && ints.Count == 0 && matrices.Count == 0 && textures.Count == 0;

        public void SetColor(string name, Color value) => colors[name] = value;
        public Color GetColor(string name) => colors.ContainsKey(name) ? colors[name] : Color.white;

        public void SetVector(string name, Vector2 value) => vectors2[name] = value;
        public Vector2 GetVector2(string name) => vectors2.ContainsKey(name) ? vectors2[name] : Vector2.Zero;
        public void SetVector(string name, Vector3 value) => vectors3[name] = value;
        public Vector3 GetVector3(string name) => vectors3.ContainsKey(name) ? vectors3[name] : Vector3.Zero;
        public void SetVector(string name, Vector4 value) => vectors4[name] = value;
        public Vector4 GetVector4(string name) => vectors4.ContainsKey(name) ? vectors4[name] : Vector4.Zero;
        public void SetFloat(string name, float value) => floats[name] = value;
        public float GetFloat(string name) => floats.ContainsKey(name) ? floats[name] : 0;
        public void SetInt(string name, int value) => ints[name] = value;
        public int GetInt(string name) => ints.ContainsKey(name) ? ints[name] : 0;
        public void SetMatrix(string name, Matrix4x4 value) => matrices[name] = value;
        public Matrix4x4 GetMatrix(string name) => matrices.ContainsKey(name) ? matrices[name] : Matrix4x4.Identity;
        public void SetTexture(string name, Texture2D value) => textures[name] = value;
        public void SetTexture(string name, AssetRef<Texture2D> value) => textures[name] = value;
        public AssetRef<Texture2D>? GetTexture(string name) => textures.ContainsKey(name) ? textures[name] : null;

        public void Clear()
        {
            textures.Clear();
            matrices.Clear();
            ints.Clear();
            floats.Clear();
            vectors2.Clear();
            vectors3.Clear();
            vectors4.Clear();
            colors.Clear();
            cachedUniformLocs.Clear();
        }

        private static int GetLoc(Raylib_cs.Shader shader, string name, MaterialPropertyBlock mpb)
        {
            int loc;
            if (!mpb.cachedUniformLocs.TryGetValue(name, out loc))
            {
                loc = Raylib.GetShaderLocation(shader, name);
                mpb.cachedUniformLocs[name] = loc;
            }
            //if (loc == -1) Debug.LogWarning("Shader does not have a uniform named " + name);
            return loc;
        }

        public static void Apply(MaterialPropertyBlock mpb, Raylib_cs.Shader shader)
        {
            Rlgl.rlEnableShader(shader.id); // Ensure the shader is enabled for this

            foreach (var item in mpb.colors)
                Raylib.SetShaderValue(shader, GetLoc(shader, item.Key, mpb), new Vector4(item.Value.r, item.Value.g, item.Value.b, item.Value.a), ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            foreach (var item in mpb.vectors2)
                Raylib.SetShaderValue(shader, GetLoc(shader, item.Key, mpb), item.Value, ShaderUniformDataType.SHADER_UNIFORM_VEC2);
            foreach (var item in mpb.vectors3)
                Raylib.SetShaderValue(shader, GetLoc(shader, item.Key, mpb), item.Value, ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            foreach (var item in mpb.vectors4)
                Raylib.SetShaderValue(shader, GetLoc(shader, item.Key, mpb), item.Value, ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            foreach (var item in mpb.floats)
                Raylib.SetShaderValue(shader, GetLoc(shader, item.Key, mpb), item.Value, ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            foreach (var item in mpb.ints)
                Raylib.SetShaderValue(shader, GetLoc(shader, item.Key, mpb), item.Value, ShaderUniformDataType.SHADER_UNIFORM_INT);
            foreach (var item in mpb.matrices)
                Raylib.SetShaderValueMatrix(shader, GetLoc(shader, item.Key, mpb), item.Value);
            int texSlot = 0;
            var keysToUpdate = new List<(string, AssetRef<Texture2D>)>();
            foreach (var item in mpb.textures)
            {
                var tex = item.Value;
                if (tex.IsAvailable)
                {
                    texSlot++;

                    // Get the memory address of the texture slot as void* using unsafe context
                    unsafe
                    {
                        Rlgl.rlSetUniform(GetLoc(shader, item.Key, mpb), &texSlot, (int)ShaderUniformDataType.SHADER_UNIFORM_INT, 1);
                    }

                    Rlgl.rlActiveTextureSlot(texSlot);
                    Rlgl.rlEnableTexture(tex.Res!.InternalTexture.id);

                    keysToUpdate.Add((item.Key, tex));
                }
            }
            foreach (var item in keysToUpdate)
                mpb.textures[item.Item1] = item.Item2;
        }
    }

    public sealed class Material : EngineObject
    {
        public AssetRef<Shader> Shader;
        public MaterialPropertyBlock PropertyBlock;

        // Key is Shader.GUID + "-" + keywords + "-" + Shader.globalKeywords
        static Dictionary<string, Raylib_cs.Shader[]> passVariants = new();
        string keywords = "";
        internal static Raylib_cs.Shader? current;

        public Material()
        {

        }

        public Material(AssetRef<Shader> shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            this.Shader = shader;
            PropertyBlock = new();
        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext context)
        {
            if(Shader.IsAvailable) CompileKeywordVariant(keywords.Split(';'));
        }

        public void EnableKeyword(string keyword)
        {
            string? key = keyword?.ToUpper().Replace(" ", "").Replace(";", "");
            if (string.IsNullOrWhiteSpace(key)) return;
            if (keywords.Contains(key)) return;
            keywords += key + ";";
        }

        public void DisableKeyword(string keyword)
        {
            string? key = keyword?.ToUpper().Replace(" ", "").Replace(";", "");
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!keywords.Contains(key)) return;
            keywords = keywords.Replace(key + ";", "");
        }

        public bool IsKeywordEnabled(string keyword) => keywords.Contains(keyword.ToUpper().Replace(" ", "").Replace(";", ""));

        public void SetPass(int pass, bool apply = false)
        {
            if (Shader.IsAvailable == false) return;
            if (current != null) throw new Exception("Pass already set");
            // Make sure we have a shader
            var shader = CompileKeywordVariant(keywords.Split(';'));

            // Make sure we have a valid pass
            if (pass < 0 || pass >= shader.Length) return;

            // Set the shader
            current = shader[pass];
            Raylib.BeginShaderMode(shader[pass]);

            if(apply)
                MaterialPropertyBlock.Apply(PropertyBlock, current.Value);
        }

        public void EndPass()
        {
            if (Shader.IsAvailable == false) return;
            Raylib.EndShaderMode();
            current = null;
        }

        Raylib_cs.Shader[] CompileKeywordVariant(string[] allKeywords)
        {
            if (Shader.IsAvailable == false) throw new Exception("Cannot compile without a valid shader assigned");
            passVariants ??= new();

            string key = Shader.AssetID.ToString() + "-" + keywords + "-" + Resources.Shader.globalKeywords;
            if (passVariants.TryGetValue(key, out var s)) return s;

            // Add each global togather making sure to not add duplicates
            string[] globals = Resources.Shader.globalKeywords.Split(';');
            for (int i = 0; i < globals.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(globals[i])) continue;
                if (allKeywords.Contains(globals[i], StringComparer.OrdinalIgnoreCase)) continue;
                allKeywords = allKeywords.Append(globals[i]).ToArray();
            }
            // Remove empty keywords
            allKeywords = allKeywords.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            // Compile Each Pass
            Raylib_cs.Shader[] compiledPasses = Shader.Res!.Compile(allKeywords);
            
            passVariants[key] = compiledPasses;
            return compiledPasses;
        }

        public void SetColor(string name, Color value) => PropertyBlock.SetColor(name, value);
        public void SetVector(string name, Vector2 value) => PropertyBlock.SetVector(name, value);
        public void SetVector(string name, Vector3 value) => PropertyBlock.SetVector(name, value);
        public void SetVector(string name, Vector4 value) => PropertyBlock.SetVector(name, value);
        public void SetFloat(string name, float value) => PropertyBlock.SetFloat(name, value);
        public void SetInt(string name, int value) => PropertyBlock.SetInt(name, value);
        public void SetMatrix(string name, Matrix4x4 value) => PropertyBlock.SetMatrix(name, value);
        public void SetTexture(string name, Texture2D value) => PropertyBlock.SetTexture(name, value);
        public void SetTexture(string name, AssetRef<Texture2D> value) => PropertyBlock.SetTexture(name, value);
        public void SetTexture(string name, Raylib_cs.Texture2D value) => PropertyBlock.SetTexture(name, new Texture2D(value));

    }

}