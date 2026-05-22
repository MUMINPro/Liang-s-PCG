using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapDisplay : MonoBehaviour
{
    //先声明一个渲染器
    //再声明一个函数把柏林噪声生成的值画在一张图上
    //最后把这张图同步到渲染器的贴图上

    public MeshFilter meshFilter;//存放网格数据（形状）,决定"长什么样"
    public MeshRenderer meshRenderer;//负责渲染（材质、贴图、光照）,决定"用什么颜色/材质画出来"
    //声明一个网格渲染器
    public Renderer textureRender;
    //声明一个渲染器（用来显示贴图的载体）
    
    //定义一个函数，接受噪声数据
    public void DrawTextureMap(Texture2D texture)
    {
        //把贴图同步到渲染器上，让 Plane 显示出来
        textureRender.sharedMaterial.mainTexture = texture;
        
        //调整Plane的大小，让比例和噪声图一致
        textureRender.transform.localScale = new Vector3(texture.width, 1, texture.height);
    }

    public void DrawMesh(MeshData meshData, Texture2D texture)
    {
        meshFilter.sharedMesh = meshData.CreatMesh();//装"形状", 调用MeshGenerator中写的方法，把 MeshData 转成真正的 Mesh
        //编辑器里预览用 shared 版本
        meshRenderer.sharedMaterial.SetTexture("_BaseMap", texture);//装颜色
    }
}
