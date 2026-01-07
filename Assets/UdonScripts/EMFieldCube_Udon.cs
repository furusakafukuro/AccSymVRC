using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class EMFieldCube_Udon : UdonSharpBehaviour
{
    [Header("Region (m)")]
    public Vector3 cubeSize_m = new Vector3(1f, 1f, 1f);

    [Header("Enable")]
    public bool enableElectricField = true;
    public bool enableMagneticField = true;

    [Header("Uniform Electric Field (V/m)  (local coordinates)")]
    public Vector3 electricField_Vm = Vector3.zero;

    [Header("Uniform Magnetic Field (Tesla) (local coordinates)")]
    public Vector3 magneticField_T = new Vector3(0f, 0f, 1f);

    private Vector3 _half;

    void Start()
    {
        _half = cubeSize_m * 0.5f;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        _half = cubeSize_m * 0.5f;
    }
#endif

    private bool IsInside(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return (Mathf.Abs(local.x) <= _half.x &&
                Mathf.Abs(local.y) <= _half.y &&
                Mathf.Abs(local.z) <= _half.z);
    }

    // Single public API
    public bool GetUniformEB(Vector3 worldPosition, out Vector3 E_Vm, out Vector3 B_T)
    {
        if (!IsInside(worldPosition))
        {
            E_Vm = Vector3.zero;
            B_T  = Vector3.zero;
            return false;
        }

        E_Vm = enableElectricField ? transform.TransformDirection(electricField_Vm) : Vector3.zero;
        B_T  = enableMagneticField ? transform.TransformDirection(magneticField_T) : Vector3.zero;
        return true;
    }

#if UNITY_EDITOR
    public bool drawGizmo = true;

    void OnDrawGizmos()
    {
        if (!drawGizmo) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.15f);
        Gizmos.DrawCube(Vector3.zero, cubeSize_m);
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.7f);
        Gizmos.DrawWireCube(Vector3.zero, cubeSize_m);
    }
#endif
}
