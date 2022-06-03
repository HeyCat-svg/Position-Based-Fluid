using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class SortDebuger : MonoBehaviour {

    Vector2Int[] a;
    public int width = 512;
    public int height = 512;
    Texture2D texture1;
    ComputeBuffer A;
    ComputeBuffer B;

    public ComputeShader cs;

    void Start() {
        texture1 = new Texture2D(width, height, TextureFormat.ARGB32, false);
        texture1.filterMode = FilterMode.Point;

        A = new ComputeBuffer(width * height, Marshal.SizeOf(typeof(Vector2Int)));
        B = new ComputeBuffer(width * height, Marshal.SizeOf(typeof(Vector2Int)));

        a = new Vector2Int[width * height];
        for (int i = 0; i < width * height; i++) {
            int tmp = (int)Random.Range(0f, 255f);
            a[i].x = tmp;
            a[i].y = tmp;
        }
        A.SetData(a);
        ApplyTexture(A);
    }

    void Update() {
        ComputeBuffer ret = GPUSort();
        ApplyTexture(ret);
    }

    void OnGUI() {
        GUI.DrawTexture(new Rect(new Vector2(0, 0), new Vector2(texture1.width, texture1.height)), texture1);
    }

    void OnDestroy() {
        A.Release();
        B.Release();
    }

    void ApplyTexture(ComputeBuffer buffer) {
        buffer.GetData(a);
        for (int i = 0; i < width * height; i++) {
            texture1.SetPixel(i % width, i / width, new Color((float)a[i].x / 256, (float)a[i].x / 256, (float)a[i].x / 256));
        }
        texture1.Apply();
    }

    ComputeBuffer GPUSort() {
        int sortNum = width * height;
        int kernel = cs.FindKernel("CompareAndExchange");
        ComputeBuffer input = A;
        ComputeBuffer output = B;

        for (uint levelMask = 0x10; levelMask <= sortNum; levelMask <<= 1) {
            cs.SetInt("_LevelMask", (int)levelMask);
            for (uint level = levelMask >> 1; level > 0; level >>= 1) {
                cs.SetInt("_Level", (int)level);
                cs.SetBuffer(kernel, "_Input", input);
                cs.SetBuffer(kernel, "_Output", output);
                cs.Dispatch(kernel, sortNum / 512, 1, 1);

                // swap buffer
                ComputeBuffer tmp = input;
                input = output;
                output = tmp;
            }
        }

        return input;
    }
}
