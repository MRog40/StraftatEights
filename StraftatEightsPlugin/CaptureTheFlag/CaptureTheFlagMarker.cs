using UnityEngine;
using UnityEngine.Rendering;

namespace StraftatEightsPlugin;

internal static class CaptureTheFlagMarker
{
    private const int RingSegments = 48;
    private const float RingOuterRadius = 0.9f;
    private const float RingInnerRadius = 0.78f;
    private const float RingHeight = 0.1f;
    private const float GroundClearance = 0.05f;
    private const float MarkerHeight = 3f;
    private const float MarkerSize = 0.7f;
    private const float PoleRadius = 0.045f;
    private static readonly GameObject?[] Markers = new GameObject?[2];
    private static readonly Renderer?[] MarkerRenderers = new Renderer?[2];
    private static readonly Renderer?[] PoleRenderers = new Renderer?[2];
    private static readonly Renderer?[] RingRenderers = new Renderer?[2];
    private static readonly int[] AttachedCarrierIds = { -1, -1 };

    internal static void Update()
    {
        if (!GameModeManager.IsActive(GameMode.CaptureTheFlag)
            || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            Clear();
            return;
        }

        for (int flagIndex = 0; flagIndex < 2; flagIndex++)
        {
            if (!CaptureTheFlagState.TryGetFlagPosition(flagIndex, out Vector3 flagPosition))
            {
                SetMarkerActive(flagIndex, false);
                continue;
            }

            if (Markers[flagIndex] == null || !Markers[flagIndex])
            {
                CreateMarker(flagIndex);
            }

            PositionMarker(flagIndex, flagPosition);
        }
    }

    internal static void ResetState()
    {
        Clear();
    }

    private static void CreateMarker(int flagIndex)
    {
        GameObject marker = new($"CaptureTheFlagMarker_{flagIndex}");
        GameObject diamond = new("Diamond");
        diamond.transform.SetParent(marker.transform, false);
        diamond.transform.localPosition = Vector3.up * MarkerHeight;
        diamond.transform.localScale = Vector3.one * MarkerSize;
        MeshFilter diamondFilter = diamond.AddComponent<MeshFilter>();
        diamondFilter.sharedMesh = CreateDiamondMesh();
        MeshRenderer diamondRenderer = diamond.AddComponent<MeshRenderer>();
        ConfigureRenderer(diamondRenderer, true);

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        pole.transform.SetParent(marker.transform, false);
        pole.transform.localPosition = Vector3.up * (MarkerHeight * 0.5f);
        pole.transform.localScale = new Vector3(PoleRadius * 2f, MarkerHeight * 0.5f,
            PoleRadius * 2f);
        Collider? poleCollider = pole.GetComponent<Collider>();
        if (poleCollider != null)
        {
            Object.Destroy(poleCollider);
        }

        MeshFilter ringFilter = marker.AddComponent<MeshFilter>();
        ringFilter.sharedMesh = CreateRingMesh();
        MeshRenderer ringRenderer = marker.AddComponent<MeshRenderer>();
        ConfigureRenderer(ringRenderer, false);

        Markers[flagIndex] = marker;
        MarkerRenderers[flagIndex] = diamondRenderer;
        PoleRenderers[flagIndex] = pole.GetComponent<Renderer>();
        RingRenderers[flagIndex] = ringRenderer;
    }

    private static void PositionMarker(int flagIndex, Vector3 flagPosition)
    {
        GameObject marker = Markers[flagIndex]!;
        marker.SetActive(true);
        bool carried = CaptureTheFlagState.GetFlagStatus(flagIndex)
            == CaptureTheFlagFlagStatus.Carried;
        int carrierId = carried ? CaptureTheFlagState.GetFlagCarrier(flagIndex) : -1;
        PlayerHealth? carrier = carried
            ? PlayerLookup.FindActivePlayerHealthById(carrierId)
            : null;
        if (carrier != null && carrier && carrier.gameObject.activeInHierarchy)
        {
            marker.transform.SetParent(carrier.transform, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            AttachedCarrierIds[flagIndex] = carrierId;
        }
        else if (carried && AttachedCarrierIds[flagIndex] == carrierId
            && marker.transform.parent != null && marker.transform.parent
            && marker.transform.parent.gameObject.activeInHierarchy)
        {
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
        }
        else
        {
            AttachedCarrierIds[flagIndex] = -1;
            marker.transform.SetParent(null, true);
            float groundY = flagPosition.y;
            if (!carried && TryGetGroundY(flagPosition, flagPosition.y, out float sampledGroundY))
            {
                groundY = sampledGroundY;
            }

            marker.transform.SetPositionAndRotation(
                new Vector3(flagPosition.x, carried ? flagPosition.y : groundY + GroundClearance,
                    flagPosition.z),
                Quaternion.identity);
        }
        Color color = GetFlagColor(flagIndex);
        SetRendererColor(MarkerRenderers[flagIndex], color);
        SetRendererColor(PoleRenderers[flagIndex], new Color(color.r, color.g, color.b, 0.65f));
        SetRendererColor(RingRenderers[flagIndex], new Color(color.r, color.g, color.b, 0.8f));
        SetMarkerActive(flagIndex, true);
    }

    private static Color GetFlagColor(int flagIndex)
    {
        TeamColorData teamColor = TeamRules.GetColor(CaptureTheFlagState.GetFlagTeam(flagIndex));
        float alpha = CaptureTheFlagState.GetFlagStatus(flagIndex)
            == CaptureTheFlagFlagStatus.Carried ? 1f : 0.85f;
        return new Color(teamColor.Red / 255f, teamColor.Green / 255f,
            teamColor.Blue / 255f, alpha);
    }

    private static Mesh CreateDiamondMesh()
    {
        Mesh mesh = new()
        {
            name = "CaptureTheFlagDiamondMesh",
            vertices = new[]
            {
                new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 0f),
                new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f)
            },
            triangles = new[]
            {
                0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2,
                1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5
            }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateRingMesh()
    {
        Mesh mesh = new() { name = "CaptureTheFlagRingMesh" };
        Vector3[] vertices = new Vector3[RingSegments * 4];
        int[] triangles = new int[RingSegments * 24];
        for (int index = 0; index < RingSegments; index++)
        {
            float angle = index * Mathf.PI * 2f / RingSegments;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);
            int vertexOffset = index * 4;
            vertices[vertexOffset] = new Vector3(x * RingOuterRadius, 0f,
                z * RingOuterRadius);
            vertices[vertexOffset + 1] = new Vector3(x * RingInnerRadius, 0f,
                z * RingInnerRadius);
            vertices[vertexOffset + 2] = new Vector3(x * RingOuterRadius, RingHeight,
                z * RingOuterRadius);
            vertices[vertexOffset + 3] = new Vector3(x * RingInnerRadius, RingHeight,
                z * RingInnerRadius);

            int nextOffset = ((index + 1) % RingSegments) * 4;
            int triangleOffset = index * 24;
            triangles[triangleOffset] = vertexOffset + 2;
            triangles[triangleOffset + 1] = nextOffset + 3;
            triangles[triangleOffset + 2] = nextOffset + 2;
            triangles[triangleOffset + 3] = vertexOffset + 2;
            triangles[triangleOffset + 4] = vertexOffset + 3;
            triangles[triangleOffset + 5] = nextOffset + 3;
            triangles[triangleOffset + 6] = vertexOffset;
            triangles[triangleOffset + 7] = nextOffset;
            triangles[triangleOffset + 8] = nextOffset + 1;
            triangles[triangleOffset + 9] = vertexOffset;
            triangles[triangleOffset + 10] = nextOffset + 1;
            triangles[triangleOffset + 11] = vertexOffset + 1;
            triangles[triangleOffset + 12] = vertexOffset;
            triangles[triangleOffset + 13] = nextOffset + 2;
            triangles[triangleOffset + 14] = nextOffset;
            triangles[triangleOffset + 15] = vertexOffset;
            triangles[triangleOffset + 16] = vertexOffset + 2;
            triangles[triangleOffset + 17] = nextOffset + 2;
            triangles[triangleOffset + 18] = vertexOffset + 1;
            triangles[triangleOffset + 19] = nextOffset + 1;
            triangles[triangleOffset + 20] = nextOffset + 3;
            triangles[triangleOffset + 21] = vertexOffset + 1;
            triangles[triangleOffset + 22] = nextOffset + 3;
            triangles[triangleOffset + 23] = vertexOffset + 3;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ConfigureRenderer(Renderer renderer, bool overlay)
    {
        Shader? shader = Shader.Find("Hidden/Internal-Colored")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            Material material = new(shader);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)(overlay ? CompareFunction.Always : CompareFunction.LessEqual));
            material.SetInt("_Cull", (int)CullMode.Off);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = overlay ? (int)RenderQueue.Overlay : 3000;
            renderer.material = material;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void SetRendererColor(Renderer? renderer, Color color)
    {
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }

    private static bool TryGetGroundY(Vector3 position, float referenceY, out float groundY)
    {
        return FloatingObjectiveMarker.TryGetGroundY(position, referenceY, out groundY);
    }

    private static void SetMarkerActive(int flagIndex, bool active)
    {
        if (Markers[flagIndex] != null && Markers[flagIndex])
        {
            Markers[flagIndex]!.SetActive(active);
        }
    }

    private static void Clear()
    {
        for (int flagIndex = 0; flagIndex < Markers.Length; flagIndex++)
        {
            if (Markers[flagIndex] != null && Markers[flagIndex])
            {
                Object.Destroy(Markers[flagIndex]);
            }

            Markers[flagIndex] = null;
            MarkerRenderers[flagIndex] = null;
            PoleRenderers[flagIndex] = null;
            RingRenderers[flagIndex] = null;
            AttachedCarrierIds[flagIndex] = -1;
        }
    }
}