using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(float[,] heightMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        
        //让网格居中
        float topLeftX = (width - 1) / -2f;
        float topLeftZ = (height - 1) / 2f;
        
        MeshData meshData = new MeshData(width, height);//填充Vertices 和 triangles数组的大小
        int vertexIndex = 0;
        
        //遍历高度图，把它转成顶点的 y（高度） 值
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                meshData.vertices[vertexIndex] = new Vector3(topLeftX + x, heightMap[x, y], topLeftZ - y);
                //topLeftX + x 、 topLeftZ - y 加减与轴向天然的遍历方向有关
                meshData.uvs[vertexIndex] = new Vector2(x / ((float)width - 1), y / ((float)height - 1));
                //这里用 x / ((float)width - 1) 是为了把值的范围映射到 0 - 1
                //width - 1是因为 uv 前面的 x 是从 0 开始，但 width 统计的宽度是从 1 开始 ， 作者这里写的有点问题
                
                // 每个顶点作为方格的"左上角"画两个三角形
                // 最右一列（x = width-1）和最下一行（y = height-1）没有右/下邻居，跳过
                if (x < width - 1 && y < height - 1)
                {
                    //正反两个三角形
                    // 两个三角形共享对角线，拼成一个方格
                    // 顶点顺时针顺序填入保证正面朝上（Y+），否则会被背面剔除而看不见
                    meshData.AddTriangle(vertexIndex , vertexIndex + width + 1 , vertexIndex + width);
                    meshData.AddTriangle(vertexIndex + width + 1  , vertexIndex , vertexIndex + 1);
                }
                vertexIndex++;
            }
        }

        return meshData;
    }
}

public class MeshData
{
    public Vector3[] vertices;//顶点数组， 存放所有顶点的3D位置
    public int[] triangles;//三角形索引数组，存放的是顶点的下标
    public Vector2[] uvs;
    
    private int trangleIndex;
    
    //创建Mesh Data对象时，根据传入的高度预先分配好两个数组的大小
    //把网格数据包装在一个类里有几个好处：
    //方便传递：可以一次把"顶点+三角形"作为一个整体传来传去
    //职责清晰：MeshGenerator 负责"算出"网格数据，调用方负责"把它变成 Mesh"
    //可复用：以后做不同地形或物体，都用这个数据结构
    public MeshData(int meshWidth, int meshHeight)
    {
        vertices = new Vector3[meshWidth * meshHeight];
        //有这么多个顶点
        triangles = new int[(meshWidth - 1) * (meshHeight - 1) * 6];
        //一个mesh有(W-1) × (H-1) 个方格， 每个方格有两个三角形，每个三角形有三个顶点
        //所以是(meshWidth - 1) * (meshHeight - 1) * 6 个索引
        uvs = new Vector2[meshWidth * meshHeight];
    }

    public void AddTriangle(int a, int b, int c)
    {
        triangles[trangleIndex] = a;
        triangles[trangleIndex + 1] = b;
        triangles[trangleIndex + 2] = c;

        trangleIndex += 3;
    }
    
    //把存的顶点/三角形数据，转成 Unity 真正能渲染的 Mesh 对象
    //Mesh是Unity内置的3D网格类型
    public Mesh CreatMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        return mesh;
    }
}
