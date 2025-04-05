using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using System.Diagnostics;
using Unity.Collections;
using System;

public class NewMonoBehaviourScript : MonoBehaviour {

    public Material m_mat;
    private ComputeBuffer m_buf = null;

    // array to store received data from the RW buffer
    private float[] m_buf_data = new float[4];

    // default data that will be set to the RW buffer after receiving data from it
    private readonly float[] m_buf_default_data = new float[] { 0.0f, 0.0f, 0.0f, 0.0f };

    private readonly int SHADER_BUF_ID = Shader.PropertyToID("_Buf");

    // same as X in: register(uX)
    private readonly int SHADER_REGISTER_IDX = 1;

    private readonly Stopwatch m_watch = new Stopwatch();

    private double m_AsyncGPUReadback_time_sum = 0.0f;
    private double m_AsyncGPUReadback_count = 0;

    // max number of AsyncGPUReadback requests dispatched to benchmark average time execution
    private readonly int m_nbr_requests = 100;

    private CommandBuffer m_cmd_buff;

    private void Init() {
        if (m_buf == null) {
            m_cmd_buff = new();
            m_buf = new ComputeBuffer(4, Marshal.SizeOf<float>(), ComputeBufferType.Default);
            m_mat.SetBuffer(SHADER_BUF_ID, m_buf);
            // this is crucial for D3D11/D3D12/Vulkan but not necessarily needed for OpenGLCore
            Graphics.ClearRandomWriteTargets();
            Graphics.SetRandomWriteTarget(SHADER_REGISTER_IDX, m_buf);
            m_buf.SetData(m_buf_default_data);
        }
    }

    private void OnValidate() {
        // uncomment this if you want to get rid of in-editor-only Attempting to draw with missing UAV bindings
        // warning for Vulkan graphics API
        // Init();
    }

    private void Start() {
        Init();
        StartCoroutine(GPUReadback());
    }


    void OnDisable() {
        if (m_buf != null) {
            m_buf.Release();
            m_buf = null;
        }
        Graphics.ClearRandomWriteTargets();
        StopAllCoroutines();
    }


    IEnumerator GPUReadback() {
        var wait_for_end_of_frame = new WaitForEndOfFrame();

        // warmup
        yield return new WaitForSecondsRealtime(5.0f);

        // direct ComputeBuffer.GetData() approach
        int nbr_frames = 100;
        double sum = 0.0;
        bool result = true;
        for (int i = 0; i < nbr_frames; ++i) {
            yield return wait_for_end_of_frame;
            result = _Internal(out double get_data_time);
            if (!result) {
                UnityEngine.Debug.LogError($"Material.SetBuffer() error. Unexpected data retrieved from Shader: {m_mat.shader.name} RW buffer.");
                break;
            }
            sum += get_data_time;
        }
        if (result) {
            UnityEngine.Debug.Log($"avg GetData time for {nbr_frames} frames: {sum / nbr_frames}ms");
            UnityEngine.Debug.Log("ComputeBuffer.GetData() calls completed successfully");
        }

        yield return wait_for_end_of_frame;

        // Async GPU buffer readback approach - doesn't block main thread like ComputeBuffer.GetData()
        m_watch.Reset();
        m_watch.Start();
        AsyncGPUReadback.Request(m_buf, AsyncGPUReadbackClbk);

        yield return new WaitUntil(() => m_AsyncGPUReadback_count == m_nbr_requests);
        yield return wait_for_end_of_frame;

        // CommandBuffer.RequestAsyncReadback approach - doesn't block main thread like ComputeBuffer.GetData()
        // reset time variables
        m_AsyncGPUReadback_time_sum = 0.0f;
        m_AsyncGPUReadback_count = 0;
        m_watch.Reset();
        m_watch.Start();
        m_cmd_buff.Clear();
        m_cmd_buff.RequestAsyncReadback(m_buf, CommandBuffer_RequestAsyncReadback_Clbk);
        Graphics.ExecuteCommandBuffer(m_cmd_buff);

        yield return new WaitUntil(() => m_AsyncGPUReadback_count == m_nbr_requests);
    }

    IEnumerator ExecuteAfterThisFrameEndsRendering(Action clbk) {
        yield return new WaitForEndOfFrame();
        clbk();
    }

    private bool _Internal(out double get_data_time) {
        m_watch.Reset();
        m_watch.Start();
        m_buf.GetData(m_buf_data);
        m_watch.Stop();
        get_data_time = m_watch.ElapsedMilliseconds;

        UnityEngine.Debug.Log($"received data: r={m_buf_data[0]}, g={m_buf_data[1]}, b={m_buf_data[2]}, a={m_buf_data[3]}");

        if (m_buf_data[0] != 1.0f || m_buf_data[3] != 1.0f) {
            return false;
        }

        // SetData is usually instantaneous
        m_buf.SetData(m_buf_default_data);
        return true;
    }

    private void AsyncGPUReadbackClbk(AsyncGPUReadbackRequest request) {
        if (request.hasError) {
            UnityEngine.Debug.LogError("AsyncGPUReadback request error");
            return;
        }
        // retrieve the UAV data and save it in cached array
        request.GetData<float>().CopyTo(m_buf_data);
        m_watch.Stop();

        UnityEngine.Debug.Log($"received data: r={m_buf_data[0]}, g={m_buf_data[1]}, b={m_buf_data[2]}, a={m_buf_data[3]}");

        if (m_buf_data[0] != 1.0f || m_buf_data[3] != 1.0f) {
            UnityEngine.Debug.LogError($"Material.SetBuffer() error. Unexpected data retrieved from Shader: {m_mat.shader.name} RW buffer.");
            return;
        }

        m_AsyncGPUReadback_time_sum += m_watch.ElapsedMilliseconds;
        ++m_AsyncGPUReadback_count;

        // reset the buffer
        m_buf.SetData(m_buf_default_data);

        if (m_AsyncGPUReadback_count < m_nbr_requests) {
            // without waiting for the next frames end, this request will return immediately with the reset data
            StartCoroutine(ExecuteAfterThisFrameEndsRendering(() => {
                m_watch.Reset();
                m_watch.Start();
                AsyncGPUReadback.Request(m_buf, AsyncGPUReadbackClbk);
            }));
            return;
        }

        UnityEngine.Debug.Log($"avg AsyncGPUReadback time for {m_nbr_requests} requests: {m_AsyncGPUReadback_time_sum / m_nbr_requests}ms");
        UnityEngine.Debug.Log("AsyncGPUReadback requests completed successfully");
    }

    private void CommandBuffer_RequestAsyncReadback_Clbk(AsyncGPUReadbackRequest request) {
        if (request.hasError) {
            UnityEngine.Debug.LogError("CommandBuffer.RequestAsyncReadback request error");
            return;
        }
        // retrieve the UAV data and save it in cached array
        request.GetData<float>().CopyTo(m_buf_data);
        m_watch.Stop();

        UnityEngine.Debug.Log($"received data: r={m_buf_data[0]}, g={m_buf_data[1]}, b={m_buf_data[2]}, a={m_buf_data[3]}");

        if (m_buf_data[0] != 1.0f || m_buf_data[3] != 1.0f) {
            UnityEngine.Debug.LogError($"Material.SetBuffer() error. Unexpected data retrieved from Shader: {m_mat.shader.name} RW buffer.");
            return;
        }

        m_AsyncGPUReadback_time_sum += m_watch.ElapsedMilliseconds;
        ++m_AsyncGPUReadback_count;

        m_cmd_buff.Clear();

        // reset the buffer
        m_cmd_buff.SetBufferData(m_buf, m_buf_default_data);
        Graphics.ExecuteCommandBuffer(m_cmd_buff);
        m_cmd_buff.Clear();

        if (m_AsyncGPUReadback_count < m_nbr_requests) {
            // without waiting for the next frames end, this request will return immediately with the reset data
            StartCoroutine(ExecuteAfterThisFrameEndsRendering(() => {
                m_watch.Reset();
                m_watch.Start();
                // reset the data then dispatch another request
                m_cmd_buff.RequestAsyncReadback(m_buf, CommandBuffer_RequestAsyncReadback_Clbk);
                Graphics.ExecuteCommandBuffer(m_cmd_buff);
            }));
            return;
        }

        UnityEngine.Debug.Log($"avg CommandBuffer.RequestAsyncReadback time for {m_nbr_requests} requests: {m_AsyncGPUReadback_time_sum / m_nbr_requests}ms");
        UnityEngine.Debug.Log("CommandBuffer RequestAsyncReadback requests completed successfully");
    }
}
