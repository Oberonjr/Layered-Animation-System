Shader "LAS/NPCSpeakingHighlight"
{
    Properties
    {
        _Color      ("Highlight Color", Color)       = (0, 1, 0, 1)
        _MinAlpha   ("Min Alpha",       Range(0, 1)) = 0.0
        _MaxAlpha   ("Max Alpha",       Range(0, 1)) = 0.35
        _PulseSpeed ("Pulse Speed",     Float)       = 2.0
        _Active     ("Active",          Float)       = 0.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off        // render both sides so the overlay is visible from all angles

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f    { float4 pos    : SV_POSITION; };

            fixed4 _Color;
            float  _MinAlpha;
            float  _MaxAlpha;
            float  _PulseSpeed;
            float  _Active;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // sin pulse lerps between _MinAlpha and _MaxAlpha, gated by _Active
                float pulse = sin(_Time.y * _PulseSpeed) * 0.5 + 0.5;
                float alpha = _Active * lerp(_MinAlpha, _MaxAlpha, pulse);
                return fixed4(_Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
