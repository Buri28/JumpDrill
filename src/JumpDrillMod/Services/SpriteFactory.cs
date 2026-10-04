using System;
using UnityEngine;

namespace JumpDrillMod.Services
{
    /// <summary>JumpDrill.Coreが返す PNG のバイト列を、画面に出せる Sprite にする。</summary>
    internal static class SpriteFactory
    {
        /// <summary>読めなければ null。</summary>
        internal static Sprite? FromPng(byte[]? png)
        {
            if (png == null || png.Length == 0) return null;

            try
            {
                // 大きさは LoadImage が PNG の中身に合わせて直すので、仮の 2x2 でよい。
                // mipmap は要らない（縮小しか掛からない）。線がにじむだけ
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
                if (!texture.LoadImage(png)) return null;

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.Apply();

                return Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Plugin.Log?.Error("could not make a sprite: " + e);
                return null;
            }
        }
    }
}
