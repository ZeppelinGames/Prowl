﻿Shader "Default/Grid"

Pass "Grid"
{
	Blend
    {
        Src Color SourceAlpha
        Src Alpha SourceAlpha

        Dest Color InverseSourceAlpha
        Dest Alpha InverseSourceAlpha

        Mode Color Add
        Mode Alpha Add
    }

    // Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite On

        // Comparison kind
        DepthTest LessEqual
    }

    // Rasterizer culling mode
    Cull None

	HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment


        struct Attributes
        {
            float3 pos : POSITION;
            float2 uv : TEXCOORD0;
        };


        struct Varyings
        {
            float4 pos : SV_POSITION;
            float3 vpos : POSITION;
            float2 uv : TEXCOORD0;
        };

        float4x4 _Matrix_MVP;
        float4x4 _Matrix_MV;

        float4 _GridColor;
		float _PrimaryGridSize;
		float _LineWidth;
		float _SecondaryGridSize;
        float _Falloff;
        float _MaxDist;


        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;

            output.pos = mul(_Matrix_MVP, float4(input.pos, 1.0));
            output.vpos = mul(_Matrix_MV, float4(input.pos, 1.0)).xyz;
            output.uv = input.uv;

            return output;
        }


		// https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8
        float pristineGrid(float2 uv, float2 _lineWidth)
        {
            _lineWidth = saturate(_lineWidth);

            float4 uvDDXY = float4(ddx(uv), ddy(uv));
            float2 uvDeriv = float2(length(uvDDXY.xz), length(uvDDXY.yw));

            bool2 invertLine = _lineWidth > 0.5;

            float2 targetWidth = select(invertLine, 1.0 - _lineWidth, _lineWidth);
            float2 drawWidth = clamp(targetWidth, uvDeriv, 0.5);

            float2 lineAA = max(uvDeriv, 0.000001) * 1.5;
            float2 gridUV = abs(frac(uv) * 2.0 - 1.0);

            gridUV = select(invertLine, gridUV, 1.0 - gridUV);

            float2 grid2 = smoothstep(drawWidth + lineAA, drawWidth - lineAA, gridUV);

            grid2 *= saturate(targetWidth / drawWidth);
            grid2 = lerp(grid2, targetWidth, saturate(uvDeriv * 2.0 - 1.0));
            grid2 = select(invertLine, 1.0 - grid2, grid2);

            return lerp(grid2.x, 1.0, grid2.y);
        }


		float4 Fragment(Varyings input) : SV_TARGET
		{
			float sg = pristineGrid(input.uv * _PrimaryGridSize, (float2)_LineWidth);
			float bg = pristineGrid(input.uv * _SecondaryGridSize, (float2)_LineWidth);

			float4 output = float4(_GridColor.xyz, max(sg, bg));

            output.w *= _GridColor.w;
            output.w *= 1 - pow((length(input.vpos) / _MaxDist), _Falloff);

            return output;
		}
	ENDHLSL
}
