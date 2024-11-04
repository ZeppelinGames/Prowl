// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Veldrid;

using static Prowl.Runtime.Light;

namespace Prowl.Runtime.Rendering.Pipelines;

public class DefaultRenderPipeline : RenderPipeline
{
    const bool cameraRelative = true;

    private static Mesh s_quadMesh;
    private static Material s_gridMaterial;
    private static Material s_defaultMaterial;
    private static Material s_skybox;
    private static Material s_gizmo;
    private static Material s_tonemapper;
    private static Mesh s_skyDome;

    public static DefaultRenderPipeline Default = new();

    private static RenderTexture? ShadowMap;
    private static GraphicsBuffer? LightBuffer;
    private static int LightCount;

    private static void ValidateDefaults()
    {
        s_quadMesh ??= Mesh.CreateQuad(Vector2.one);
        s_gridMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Grid.shader"));
        s_defaultMaterial ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Standard.shader"));
        s_skybox ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ProceduralSky.shader"));
        s_gizmo ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/Gizmo.shader"));
        s_tonemapper ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/ToneMapper.shader"));

        if (s_skyDome == null)
        {
            GameObject skyDomeModel = Application.AssetProvider.LoadAsset<GameObject>("Defaults/SkyDome.obj").Res;
            MeshRenderer renderer = skyDomeModel.GetComponentInChildren<MeshRenderer>(true, true);

            s_skyDome = renderer.Mesh.Res;
        }
    }


    public override void Render(Framebuffer target, Camera camera, in RenderingData data)
    {
        ValidateDefaults();

        CommandBuffer buffer = CommandBufferPool.Get("Rendering Command Buffer");

        bool clearColor = camera.ClearMode == CameraClearMode.ColorOnly || camera.ClearMode == CameraClearMode.DepthColor;
        bool clearDepth = camera.ClearMode == CameraClearMode.DepthOnly || camera.ClearMode == CameraClearMode.DepthColor;
        bool drawSkybox = camera.ClearMode == CameraClearMode.Skybox;

        RenderTexture forward = RenderTexture.GetTemporaryRT(target.Width, target.Height, [PixelFormat.R16_G16_B16_A16_Float]);

        buffer.SetRenderTarget(forward);
        buffer.ClearRenderTarget(clearDepth || drawSkybox, clearColor || drawSkybox, camera.ClearColor);

        Matrix4x4 view = camera.GetViewMatrix(!cameraRelative);
        Vector3 cameraPosition = camera.Transform.position;

        Matrix4x4 projection = camera.GetProjectionMatrix(data.TargetResolution, true);

        Matrix4x4 vp = view * projection;

        BoundingFrustum worldFrustum = camera.GetFrustum(data.TargetResolution);

        Vector3 sunDirection = Vector3.up;

        List<IRenderableLight> lights = GetLights();
        if (lights.Count > 0)
        {
            IRenderableLight light0 = lights[0];

            if (light0.GetLightType() == LightType.Directional)
                sunDirection = light0.GetLightDirection();
        }

        PrepareShadowAtlas();

        CreateLightBuffer(buffer, camera, lights);
        buffer.SetRenderTarget(forward); // Return target, as shadow map rendering may have changed it
        buffer.SetTexture("_ShadowAtlas", ShadowMap.DepthBuffer);
        buffer.SetBuffer("_Lights", LightBuffer);
        buffer.SetInt("_LightCount", LightCount);
        buffer.SetVector("_CameraWorldPos", cameraPosition);

        if (drawSkybox)
        {
            buffer.SetMaterial(s_skybox);

            buffer.SetMatrix("_Matrix_VP", (camera.GetViewMatrix(false) * projection).ToFloat());
            buffer.SetVector("_SunDir", sunDirection);

            buffer.DrawSingle(s_skyDome);
        }

        DrawRenderables(buffer, cameraPosition, vp, view, projection, worldFrustum);

        if (data.DisplayGrid)
        {
            const float gridScale = 1000;

            Matrix4x4 grid = Matrix4x4.CreateScale(gridScale);

            grid *= data.GridMatrix;

            if (cameraRelative)
                grid.Translation -= cameraPosition;

            Matrix4x4 MV = grid * view;
            Matrix4x4 MVP = grid * view * projection;

            buffer.SetMatrix("_Matrix_MV", MV.ToFloat());
            buffer.SetMatrix("_Matrix_MVP", MVP.ToFloat());

            buffer.SetColor("_GridColor", data.GridColor);
            buffer.SetFloat("_LineWidth", (float)data.GridSizes.z);
            buffer.SetFloat("_PrimaryGridSize", 1 / (float)data.GridSizes.x * gridScale * 2);
            buffer.SetFloat("_SecondaryGridSize", 1 / (float)data.GridSizes.y * gridScale * 2);
            buffer.SetFloat("_Falloff", 15.0f);
            buffer.SetFloat("_MaxDist", Math.Min(camera.FarClip, gridScale));

            buffer.SetMaterial(s_gridMaterial, 0);
            buffer.DrawSingle(s_quadMesh);
        }

        // Since for the time being rendering is guranteed to be executed after Update() and all other mono-behaviour methods
        // we can safely assume that all gizmos have been added to Debug and we can draw them here
        if (data.DisplayGizmo)
        {
            (Mesh? wire, Mesh? solid) = Debug.GetGizmoDrawData(cameraRelative, cameraPosition);

            if (wire != null || solid != null)
            {
                // The vertices have already been transformed by the gizmo system to be camera relative (if needed) so we just need to draw them
                buffer.SetMatrix("_Matrix_VP", vp.ToFloat());

                buffer.SetTexture("_MainTexture", Texture2D.White.Res);
                buffer.SetMaterial(s_gizmo);
                if (wire != null) buffer.DrawSingle(wire);
                if (solid != null) buffer.DrawSingle(solid);
            }

            List<GizmoBuilder.IconDrawCall> icons = Debug.GetGizmoIcons();
            if (icons != null)
            {
                buffer.SetMaterial(s_gizmo);

                foreach (GizmoBuilder.IconDrawCall icon in icons)
                {
                    Vector3 center = icon.center;
                    if (cameraRelative)
                        center -= cameraPosition;
                    Matrix4x4 billboard = Matrix4x4.CreateBillboard(center, Vector3.zero, camera.Transform.up, camera.Transform.forward);

                    buffer.SetMatrix("_Matrix_VP", (billboard * vp).ToFloat());
                    buffer.SetTexture("_MainTexture", icon.texture);

                    buffer.DrawSingle(s_quadMesh);
                }
            }
        }

        /*
        if (target.ColorTargets != null && target.ColorTargets.Length > 0)
        {
            RenderTexture temporaryRT = RenderTexture.GetTemporaryRT(target.Width, target.Height, null, [target.ColorTargets[0].Target.Format]);

            buffer.SetRenderTarget(temporaryRT);

            buffer.SetTexture("_MainTexture", target.ColorTargets[0].Target);

            RenderTexture.ReleaseTemporaryRT(temporaryRT);
        }
        */

        Graphics.SubmitCommandBuffer(buffer);
        CommandBufferPool.Release(buffer);

        s_tonemapper.SetFloat("_Contrast", 1f);
        s_tonemapper.SetFloat("_Saturation", 1f);
        Graphics.Blit(forward, target, s_tonemapper);

        RenderTexture.ReleaseTemporaryRT(forward);
    }

    private static void DrawRenderables(CommandBuffer buffer, Vector3 cameraPosition, Matrix4x4 vp, Matrix4x4 view, Matrix4x4 proj, BoundingFrustum? worldFrustum = null, bool shadowPass = false)
    {
        foreach (RenderBatch batch in EnumerateBatches())
        {
            if (!shadowPass)
            {
                buffer.SetMaterial(batch.material);
            }
            else
            {
                int pass = batch.material.Shader.Res.GetPassIndex("ShadowPass");
                buffer.SetMaterial(batch.material, pass != -1 ? pass : 0);
            }

            foreach (int renderIndex in batch.renderIndices)
            {
                IRenderable renderable = GetRenderable(renderIndex);

                if (worldFrustum != null && CullRenderable(renderable, worldFrustum))
                    continue;

                renderable.GetRenderingData(out PropertyState properties, out IGeometryDrawData drawData, out Matrix4x4 model);

                if (cameraRelative)
                    model.Translation -= cameraPosition;

                // model = Graphics.GetGPUModelMatrix(model);

                buffer.ApplyPropertyState(properties);

                buffer.SetMatrix("Mat_V", view.ToFloat());
                buffer.SetMatrix("Mat_P", proj.ToFloat());
                buffer.SetMatrix("Mat_ObjectToWorld", model.ToFloat());
                buffer.SetMatrix("Mat_WorldToObject", model.Invert().ToFloat());
                buffer.SetMatrix("Mat_MVP", (model * vp).ToFloat());
                
                buffer.SetColor("_MainColor", Color.white);

                buffer.UpdateBuffer("_PerDraw");


                buffer.SetDrawData(drawData);
                buffer.DrawIndexed((uint)drawData.IndexCount, 0, 1, 0, 0);
            }
        }
    }

    private static void PrepareShadowAtlas()
    {
        ShadowAtlas.TryInitialize();

        ShadowAtlas.Clear();
        CommandBuffer atlasClear = CommandBufferPool.Get("Shadow Atlas Clear");
        atlasClear.SetRenderTarget(ShadowAtlas.GetAtlas());
        atlasClear.ClearRenderTarget(true, false, Color.black);

        Graphics.SubmitCommandBuffer(atlasClear);
        CommandBufferPool.Release(atlasClear);
    }
    
    private static void CreateLightBuffer(CommandBuffer buffer, Camera cam, List<IRenderableLight> lights)
    {
        // We have AtlasWidth slots for shadow maps
        // a single shadow map can consume multiple slots if its larger then 128x128
        // We need to distribute these slots and resolutions out to lights
        // based on their distance from the camera
        int width = ShadowAtlas.GetAtlasWidth();

        // Sort lights by distance from camera
        //lights = lights.OrderBy(l => {
        //    if (l is DirectionalLight)
        //        return 0; // Directional Lights always get highest priority
        //    else
        //        return Vector3.Distance(cam.Transform.position, l.GetLightPosition());
        //}).ToList();

        List<GPULight> gpuLights = [];
        foreach (var light in lights)
        {
            // Calculate resolution based on distance
            int res = CalculateResolution(Vector3.Distance(cam.Transform.position, light.GetLightPosition())); // Directional lights are always 1024
            if (light is DirectionalLight dir)
                res = (int)dir.shadowResolution;

            if (light.DoCastShadows())
            {
                var gpu = light.GetGPULight(ShadowAtlas.GetSize(), cameraRelative, cam.Transform.position);
                
                // Find a slot for the shadow map
                var slot = ShadowAtlas.ReserveTiles(res, res, light.GetLightID());

                if (slot != null)
                {
                    gpu.AtlasX = slot.Value.x;
                    gpu.AtlasY = slot.Value.y;
                    gpu.AtlasWidth = res;

                    // Draw the shadow map
                    ShadowMap = ShadowAtlas.GetAtlas();

                    buffer.SetRenderTarget(ShadowMap);
                    buffer.SetViewports(slot.Value.x, slot.Value.y, res, res, 0, 1000);

                    light.GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 proj);
                    BoundingFrustum frustum = new(view * proj);
                    if (cameraRelative)
                        view.Translation = Vector3.zero;
                    Matrix4x4 lightVP = view * proj;


                    DrawRenderables(buffer, light.GetLightPosition(), lightVP, view, proj, frustum, true);

                    buffer.SetFullViewports();
                }
                else
                {
                    gpu.AtlasX = -1;
                    gpu.AtlasY = -1;
                    gpu.AtlasWidth = 0;
                }

                gpuLights.Add(gpu);
            }
            else
            {
                GPULight gpu = light.GetGPULight(0, cameraRelative, cam.Transform.position);
                gpu.AtlasX = -1;
                gpu.AtlasY = -1;
                gpu.AtlasWidth = 0;
                gpuLights.Add(gpu);
            }
        }


        unsafe
        {
            if (LightBuffer == null || gpuLights.Count > LightCount)
            {
                LightBuffer?.Dispose();
                LightBuffer = new((uint)gpuLights.Count, (uint)sizeof(GPULight), false);
            }
            
            if (gpuLights.Count > 0)
                LightBuffer.SetData<GPULight>(gpuLights.ToArray(), 0);
            //else Dont really need todo this since LightCount will be 0
            //    LightBuffer = GraphicsBuffer.Empty;

            LightCount = lights.Count;
        }
    }

    private static int CalculateResolution(double distance)
    {
        double t = MathD.Clamp(distance / 16f, 0, 1);
        var tileSize = ShadowAtlas.GetTileSize();
        int resolution = MathD.RoundToInt(MathD.Lerp(ShadowAtlas.GetMaxShadowSize(), tileSize, t));

        // Round to nearest multiple of tile size
        return MathD.Max(tileSize, (resolution / tileSize) * tileSize);
    }

    private static bool CullRenderable(IRenderable renderable, BoundingFrustum cameraFrustum)
    {
        renderable.GetCullingData(out bool isRenderable, out Bounds bounds);

        return !isRenderable || !cameraFrustum.Intersects(bounds);
    }
}
