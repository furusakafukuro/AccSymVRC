using System.Diagnostics;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class BeamParticle : UdonSharpBehaviour
{
    [Header("Runtime State (read-only)")]
    [SerializeField] private bool alive;
    [SerializeField] private float age;
    [SerializeField] private float lifeTime;

    // Physics state (Unity units)
    [SerializeField] private Vector3 positionU;
    [SerializeField] private Vector3 velocityU;

    // Cached
    private Transform tr;
    private Renderer rend;
    private VRCPlayerApi localPlayer;

    public bool Alive => alive;

    private void Start()
    {
        tr = transform;
        rend = GetComponent<Renderer>();
        localPlayer = Networking.LocalPlayer;
        SetAlive(false);
    }

    public void Init(Vector3 posU, Vector3 velU, float lifeTimeSec)
    {
        positionU = posU;
        velocityU = velU;
        age = 0f;
        lifeTime = Mathf.Max(0.01f, lifeTimeSec);
        SetAlive(true);
        ApplyTransform();
    }

    public void Kill()
    {
        SetAlive(false);
    }

    public void StepKinematic(float dt)
    {
        if (!alive) return;

        age += dt;
        if (age >= lifeTime)
        {
            SetAlive(false);
            return;
        }

        positionU += velocityU * dt;
        ApplyTransform();
        BillboardToLocalPlayer();
        // UnityEngine.Debug.Log($"dt = {dt}");
        // UnityEngine.Debug.Log("BeamParticle StepKinematic: pos=" + positionU.ToString("F3"));
    }

    public void StepWithAcceleration(float dt, Vector3 accelU)
    {
        if (!alive) return;

        age += dt;
        if (age >= lifeTime)
        {
            SetAlive(false);
            return;
        }

        velocityU += accelU * dt;
        positionU += velocityU * dt;

        ApplyTransform();
        BillboardToLocalPlayer();
    }

    private void ApplyTransform()
    {
        if (tr != null) tr.localPosition = positionU;
    }

    private void BillboardToLocalPlayer()
    {
        // “点群 billboard” の最小実装：常にローカルプレイヤーの頭方向を見る
        if (localPlayer == null) return;
        VRCPlayerApi.TrackingData td = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
        Vector3 headPos = td.position;
        Vector3 worldPos = tr.position;

        Vector3 dir = - headPos + worldPos;
        if (dir.sqrMagnitude < 1e-12f) return;

        tr.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private void SetAlive(bool v)
    {
        alive = v;

        // 表示/非表示
        if (rend != null) rend.enabled = v;

        // Collider等があれば同時に切る（必要なら）
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = v;
    }
}