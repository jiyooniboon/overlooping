using UnityEngine;

namespace KnitSprite.CreativeMode
{
    /// <summary>
    /// sRGB to CIE L*a*b* conversion and perceptual distance.
    ///
    /// Why Lab and not RGB: RGB distance does not match how different two colors *look*. Two
    /// greens that read as clearly distinct yarns can be closer in RGB than a green and a grey
    /// that read as obviously different. Clustering and palette-snapping in RGB is the main
    /// reason naive pixel-art converters produce muddy results; Lab is roughly perceptually
    /// uniform, so nearest-color decisions match human judgement.
    ///
    /// Values verified against reference conversions (D65 illuminant, sRGB primaries):
    ///   (255,0,0)   -> L 53.24  a  80.09  b  67.20
    ///   (0,255,0)   -> L 87.73  a -86.18  b  83.18
    ///   (0,0,255)   -> L 32.30  a  79.19  b -107.86
    ///   (128,128,128) -> L 53.59, a/b ~0
    /// </summary>
    public struct Lab
    {
        public float L, a, b;

        public Lab(float L, float a, float b)
        {
            this.L = L; this.a = a; this.b = b;
        }

        /// <summary>Squared Lab distance. Squared to avoid a sqrt in inner loops - only ordering matters.</summary>
        public static float SqrDistance(Lab p, Lab q)
        {
            float dL = p.L - q.L, da = p.a - q.a, db = p.b - q.b;
            return dL * dL + da * da + db * db;
        }
    }

    public static class ColorMath
    {
        // D65 reference white
        const float Xn = 0.95047f, Yn = 1.00000f, Zn = 1.08883f;

        static float SrgbToLinear(byte channel)
        {
            float c = channel / 255f;
            return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        static float PivotXyz(float t)
        {
            return t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : (7.787f * t + 16f / 116f);
        }

        public static Lab RgbToLab(Color32 c)
        {
            float r = SrgbToLinear(c.r);
            float g = SrgbToLinear(c.g);
            float b = SrgbToLinear(c.b);

            float X = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
            float Y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
            float Z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;

            float fx = PivotXyz(X / Xn);
            float fy = PivotXyz(Y / Yn);
            float fz = PivotXyz(Z / Zn);

            return new Lab(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        /// <summary>
        /// Nearest palette entry to a Lab color. minIndex lets callers exclude the background
        /// entry (index 0) so foreground detail can never collapse into the fabric color.
        /// </summary>
        public static int NearestPaletteIndex(Lab target, Lab[] paletteLab, int minIndex = 0)
        {
            int best = minIndex;
            float bestD = float.MaxValue;
            for (int i = minIndex; i < paletteLab.Length; i++)
            {
                float d = Lab.SqrDistance(target, paletteLab[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static Lab[] PaletteToLab(YarnPalette palette)
        {
            var arr = new Lab[palette.Count];
            for (int i = 0; i < palette.Count; i++)
                arr[i] = RgbToLab(palette.GetColor(i));
            return arr;
        }
    }
}
