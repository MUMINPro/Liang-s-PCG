using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class TextureGenerator
{
    //把传入的颜色数组生成为一张图片
    public static Texture2D TextureFromColorMap(Color[] colourMap, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height);
        texture.filterMode = FilterMode.Point;
        //设置贴图的过滤模式为"点采样", 不要做平滑插值，保持像素的"硬边"
        texture.wrapMode = TextureWrapMode.Clamp;
        //设置贴图的包裹模式为"夹紧", 超出贴图范围的部分，使用边缘像素填充，而不是重复贴图。
        texture.SetPixels(colourMap);
        texture.Apply();
        return texture;
    }
    
    //把传入的噪声数组生成为一张图片
    public static Texture2D TextureFromHeightMap(float[,] heightMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        
        Color[] colorMap = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                colorMap[y * width + x] = Color.Lerp(Color.black, Color.white, heightMap[x, y]);
                //把每个噪声值（0 - 1）转成对应的颜色
                //存入colorMap数组
            }
        }

        return TextureFromColorMap(colorMap, width, height);
    }
}
