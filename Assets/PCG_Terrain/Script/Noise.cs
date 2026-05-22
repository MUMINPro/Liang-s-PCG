using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Noise
{
    //Octaves 八度数 叠几层噪声？
    //persistance 持久度 每层振幅怎么变化（每层振幅怎么变）？
    //lacunarity 孔隙度 每层频率怎么变（细节密度）
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, int seed, float scale, int octaves, float persistance, float lacunarity, Vector2 offset)
    {
        //用一个二维数组记录noiseMap的每个像素的值
        float[,] nosieMap = new float[mapWidth, mapHeight];
        
        //防止scale <= 0
        if (scale <= 0)
        {
            scale = 0.0001f;
        }
        
        //生成随机种子
        System.Random prng = new System.Random(seed); //这个是unity自己的Random //seed 让
        //根据设定的层数声明一个 二维向量 数组
        Vector2[] octaveOffsets = new Vector2[octaves];
        //为每一层赋予一个偏移量
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + offset.x;//Lague自己试出来在(-100000, 100000)不容易看到重复的
            float offsetY = prng.Next(-100000, 100000) + offset.y;//把UI的偏移加进去来，实现控制
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }
        Debug.Log($"seed={seed}, octaves={octaves}, offset[0]={octaveOffsets[0]}");
        
        //用来记录生成的柏林噪声图中最大和最小的值
        float maxNoiseHeight = float.MinValue;//赋予float所能表示的最小值
        float minNoiseHeight = float.MaxValue;//赋予float所能表示的最大值
        
        //以中心缩放
        float halfWidth = mapWidth / 2.0f;
        float halfHeight = mapHeight / 2.0f;
        
        for (int y = 0; y < mapHeight; y++)//遍历所有行
        {
            for (int x = 0; x < mapWidth; x++)//遍历每一行的所有像素
            {
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;
                for (int i = 0; i < octaves; i++)//该循环内是单个像素最终累加的值
                {
                    float sampleX = (x - halfWidth) / scale * frequency + octaveOffsets[i].x;
                    float sampleY = (y - halfHeight) / scale * frequency + octaveOffsets[i].y;

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleY) * 2 - 1;
                    //* 2 - 1 把噪声从"全是正数"变成"有正有负"，这样多层叠加时才能真正出现山峰和山谷，不会全部往高处堆。
                    //否则原本全是正数只会越来越高最后飘在空中
                    noiseHeight += perlinValue * amplitude;//amplitude控制这一层柏林噪声的权重，原来是 1

                    amplitude *= persistance;//该循环内每次 amplitude 的值都会越来越小 ， persistance典型值是0.5
                    frequency *= lacunarity;//该循环内每次 frequency 的值都会越来越大， lacunarity的典型值是2
                }
                
                //记录生成的所有柏林噪声的值中最大和最小的值
                // 因为多层叠加后 noiseHeight 范围不可控（可能负数、可能超过1）
                //通过这个最大最小值可以很方便的把值域映射回 0-1
                if (noiseHeight > maxNoiseHeight)
                {
                    maxNoiseHeight = noiseHeight;
                }
                else if (noiseHeight < minNoiseHeight)
                {
                    minNoiseHeight = noiseHeight;
                }
                
                nosieMap[x, y] = noiseHeight;
                //把最终算出来的这一下标的柏林噪声的原始值值存入 noiseMap 数组（未归一化）
            }
        }
        
        //归一化
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                nosieMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, nosieMap[x, y]);
                //根据之前记录的柏林噪声数组中的最大值和最小值 以及 这个像素的值所在的位置
                //把值域映射到 0 - 1
            }
        }
        Debug.Log($"max={maxNoiseHeight}, min={minNoiseHeight}");

        return nosieMap;
    }
}
