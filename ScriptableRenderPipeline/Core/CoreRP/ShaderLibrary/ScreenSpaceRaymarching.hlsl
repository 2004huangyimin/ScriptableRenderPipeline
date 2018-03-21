#ifndef UNITY_SCREEN_SPACE_RAYMARCHING_INCLUDED
#define UNITY_SCREEN_SPACE_RAYMARCHING_INCLUDED

// -------------------------------------------------
// Algorithm uniform parameters
// -------------------------------------------------

CBUFFER_START(ScreenSpaceRaymarching)
// HiZ      : Min mip level
// Linear   : Mip level
// Estimate : Mip Level
int _SSRayMinLevel;
// HiZ      : Max mip level
int _SSRayMaxLevel;
CBUFFER_END

// -------------------------------------------------
// Output
// -------------------------------------------------

struct ScreenSpaceRayHit
{
    float distance;         // Distance raymarched
    float linearDepth;      // Linear depth of the hit point
    uint2 positionSS;       // Position of the hit point (SS)
    float2 positionNDC;     // Position of the hit point (NDC)

#ifdef DEBUG_DISPLAY
    float3 debugOutput;
#endif
};

// -------------------------------------------------
// Utilities
// -------------------------------------------------

// Calculate the ray origin and direction in TXS
// out positionTXS  : (x, y, 1/depth)
// out rayTXS       : (x, y, 1/depth)
void CalculateRayTXS(
    float3 rayOriginVS,
    float3 rayDirVS,
    float4x4 projectionMatrix,
    uint2 bufferSize,
    out float3 positionTXS,
    out float3 rayTXS)
{
    float3 positionVS = rayOriginVS;
    float3 rayEndVS = rayOriginVS + rayDirVS * 10;

    float4 positionCS = ComputeClipSpacePosition(positionVS, projectionMatrix);
    float4 rayEndCS = ComputeClipSpacePosition(rayEndVS, projectionMatrix);

    float2 positionNDC = ComputeNormalizedDeviceCoordinates(positionVS, projectionMatrix);
    float2 rayEndNDC = ComputeNormalizedDeviceCoordinates(rayEndVS, projectionMatrix);

    float3 rayStartTXS = float3(
        positionNDC.xy * bufferSize,
        1.0 / positionCS.w); // Screen space depth interpolate properly in 1/z

    float3 rayEndTXS = float3(
        rayEndNDC.xy * bufferSize,
        1.0 / rayEndCS.w); // Screen space depth interpolate properly in 1/z

    positionTXS = rayStartTXS;
    rayTXS = rayEndTXS - rayStartTXS;
}

// Check whether the depth of the ray is above the sampled depth
// Arguments are inversed linear depth
bool IsPositionAboveDepth(float rayDepth, float invLinearDepth)
{
    // as depth is inverted, we must invert the check as well
    // rayZ > HiZ <=> 1/rayZ < 1/HiZ
    return rayDepth > invLinearDepth;
}

// Sample the Depth buffer at a specific mip and linear depth
float LoadDepth(float2 positionTXS, int level)
{
    float pyramidDepth = LOAD_TEXTURE2D_LOD(_PyramidDepthTexture, int2(positionTXS.xy) >> level, level).r;
    float linearDepth = LinearEyeDepth(pyramidDepth, _ZBufferParams);
    return linearDepth;
}

// Sample the Depth buffer at a specific mip and return 1/linear depth
float LoadInvDepth(float2 positionTXS, int level)
{
    float linearDepth = LoadDepth(positionTXS, level);
    float invLinearDepth = 1 / linearDepth;
    return invLinearDepth;
}

bool CellAreEquals(int2 cellA, int2 cellB)
{
    return cellA.x == cellB.x && cellA.y == cellB.y;
}

// Calculate intersection between the ray and the depth plane
// positionTXS.z is 1/depth
// rayTXS.z is 1/depth
float3 IntersectDepthPlane(float3 positionTXS, float3 rayTXS, float invDepth, out float distance)
{
    const float EPSILON = 1E-5;

    // The depth of the intersection with the depth plane is: positionTXS.z + rayTXS.z * t = invDepth
    distance = (invDepth - positionTXS.z) / rayTXS.z;

    // (t<0) When the ray is going away from the depth plane,
    //  put the intersection away.
    // Instead the intersection with the next tile will be used.
    // (t>=0) Add a small distance to go through the depth plane.
    distance = distance >= 0.0f ? (distance + EPSILON) : 1E5;

    // Return the point on the ray
    return positionTXS + rayTXS * distance;
}

// Calculate intersection between a ray and a cell
float3 IntersectCellPlanes(
    float3 positionTXS,
    float3 rayTXS,
    float2 invRayTXS,
    int2 cellId,
    uint2 cellSize,
    int2 cellPlanes,
    float2 crossOffset,
    out float distance)
{
    // Planes to check
    int2 planes = (cellId + cellPlanes) * cellSize;
    // Hit distance to each planes
    float2 distanceToCellAxes = float2(planes - positionTXS.xy) * invRayTXS; // (distance to x axis, distance to y axis)
    distance = min(distanceToCellAxes.x, distanceToCellAxes.y);
    // Interpolate screen space to get next test point
    float3 testHitPositionTXS = positionTXS + rayTXS * distance;

    // Offset the proper axis to enforce cell crossing
    // https://gamedev.autodesk.com/blogs/1/post/5866685274515295601
    testHitPositionTXS.xy += (distanceToCellAxes.x < distanceToCellAxes.y)
        ? float2(crossOffset.x, 0)
        : float2(0, crossOffset.y);

    return testHitPositionTXS;
}

#ifdef DEBUG_DISPLAY
// -------------------------------------------------
// Debug Utilities
// -------------------------------------------------

void FillScreenSpaceRaymarchingHitDebug(
    uint2 bufferSize,
    float3 rayDirVS,
    float3 rayTXS,
    float3 startPositionTXS,
    bool hitSuccessful,
    int iteration,
    int maxIterations,
    int maxUsedLevel,
    int maxMipLevel,
    inout ScreenSpaceRayHit hit)
{
    float3 debugOutput = float3(0, 0, 0);
    if (_DebugLightingMode == DEBUGLIGHTINGMODE_SCREEN_SPACE_TRACING_REFRACTION)
    {
        switch (_DebugLightingSubMode)
        {
        case DEBUGSCREENSPACETRACING_POSITION_NDC:
            debugOutput =  float3(float2(startPositionTXS.xy) / bufferSize, 0);
            break;
        case DEBUGSCREENSPACETRACING_DIR_VS:
            debugOutput =  rayDirVS * 0.5 + 0.5;
            break;
        case DEBUGSCREENSPACETRACING_DIR_NDC:
            debugOutput =  float3(rayTXS.xy * 0.5 + 0.5, frac(0.1 / rayTXS.z));
            break;
        case DEBUGSCREENSPACETRACING_HIT_DISTANCE:
            debugOutput =  frac(hit.distance * 0.1);
            break;
        case DEBUGSCREENSPACETRACING_HIT_DEPTH:
            debugOutput =  frac(hit.linearDepth * 0.1);
            break;
        case DEBUGSCREENSPACETRACING_HIT_SUCCESS:
            debugOutput =  hitSuccessful;
            break;
        case DEBUGSCREENSPACETRACING_ITERATION_COUNT:
            debugOutput =  float(iteration) / float(maxIterations);
            break;
        case DEBUGSCREENSPACETRACING_MAX_USED_LEVEL:
            debugOutput =  float(maxUsedLevel) / float(maxMipLevel);
            break;
        }
    }
    hit.debugOutput = debugOutput;
}

void FillScreenSpaceRaymarchingPreLoopDebug(
    float3 startPositionTXS,
    inout ScreenSpaceTracingDebug debug)
{
    debug.startPositionSSX = uint(startPositionTXS.x);
    debug.startPositionSSY = uint(startPositionTXS.y);
    debug.startLinearDepth = 1 / startPositionTXS.z;
}

void FillScreenSpaceRaymarchingPostLoopDebug(
    int maxUsedLevel,
    int iteration,
    float3 rayTXS,
    ScreenSpaceRayHit hit,
    inout ScreenSpaceTracingDebug debug)
{
    debug.levelMax = maxUsedLevel;
    debug.iterationMax = iteration;
    debug.hitDistance = hit.distance;
    debug.rayTXS = rayTXS;
}

void FillScreenSpaceRaymarchingPreIterationDebug(
    int iteration,
    int currentLevel,
    inout ScreenSpaceTracingDebug debug)
{
    if (_DebugStep == iteration)
        debug.level = currentLevel;
}

void FillScreenSpaceRaymarchingPostIterationDebug(
    int iteration,
    uint2 cellSize,
    float3 positionTXS,
    float iterationDistance,
    float invHiZDepth,
    inout ScreenSpaceTracingDebug debug)
{
    if (_DebugStep == iteration)
    {
        debug.cellSizeW = cellSize.x;
        debug.cellSizeH = cellSize.y;
        debug.positionTXS = positionTXS;
        debug.hitLinearDepth = 1 / positionTXS.z;
        debug.hitPositionSS = uint2(positionTXS.xy);
        debug.iteration = iteration;
        debug.iterationDistance = iterationDistance;
        debug.hiZLinearDepth = 1 / invHiZDepth;
    }
}
#endif

// -------------------------------------------------
// Algorithm: rough estimate
// -------------------------------------------------

struct ScreenSpaceEstimateRaycastInput
{
    float2 referencePositionNDC;            // Position of the reference (NDC)
    float3 referencePositionWS;             // Position of the reference (WS)
    float referenceLinearDepth;             // Linear depth of the reference
    float3 rayOriginWS;                     // Origin of the ray (WS)
    float3 rayDirWS;                        // Direction of the ray (WS)
    float3 depthNormalWS;                   // Depth plane normal (WS)
    float4x4 viewProjectionMatrix;          // View Projection matrix of the camera

#ifdef DEBUG_DISPLAY
    bool writeStepDebug;
#endif
};

// Fast but very rough estimation of scene screen space raycasting.
// * We approximate the scene as a depth plane and raycast against that plane.
// * The reference position is usually the pixel being evaluated in front of opaque geometry
// * So the reference position is used to sample and get the depth plane
// * The reference depth is usually, the depth of the transparent object being evaluated
bool ScreenSpaceEstimateRaycast(
    ScreenSpaceEstimateRaycastInput input,
    out ScreenSpaceRayHit hit)
{
    uint mipLevel = clamp(_SSRayMinLevel, 0, int(_PyramidDepthMipSize.z));
    uint2 bufferSize = uint2(_PyramidDepthMipSize.xy);

    // Get the depth plane
    float depth = LoadDepth(input.referencePositionNDC * (bufferSize >> mipLevel), mipLevel);

    // Calculate projected distance from the ray origin to the depth plane
    float depthFromReference = depth - input.referenceLinearDepth;
    float offset = dot(input.depthNormalWS, input.rayOriginWS - input.referencePositionWS);
    float depthFromRayOrigin = depthFromReference - offset;

    // Calculate actual distance from ray origin to depth plane
    float hitDistance = depthFromRayOrigin / dot(input.depthNormalWS, input.rayDirWS);
    float3 hitPositionWS = input.rayOriginWS + input.rayDirWS * hitDistance;

    hit.distance = hitDistance;
    hit.positionNDC = ComputeNormalizedDeviceCoordinates(hitPositionWS, input.viewProjectionMatrix);
    hit.positionSS = hit.positionNDC * bufferSize;
    hit.linearDepth = LoadDepth(hit.positionSS, 0);


#ifdef DEBUG_DISPLAY
    FillScreenSpaceRaymarchingHitDebug(
        bufferSize, 
        float3(0, 0, 0),    // rayDirVS
        float3(0, 0, 0),    // rayTXS
        float3(0, 0, 0),    // startPositionTXS
        true,               // hitSuccessful
        1,                  // iteration
        1,                  // iterationMax
        0,                  // maxMipLevel
        0,                  // maxUsedLevel
        hit);
    if (input.writeStepDebug)
    {
        ScreenSpaceTracingDebug debug;
        ZERO_INITIALIZE(ScreenSpaceTracingDebug, debug);
        FillScreenSpaceRaymarchingPreLoopDebug(float3(0, 0, 0), debug);
        FillScreenSpaceRaymarchingPreIterationDebug(1, mipLevel, debug);
        FillScreenSpaceRaymarchingPostIterationDebug(
            1,                              // iteration
            uint2(1, 1),                    // cellSize
            float3(0, 0, 0),                // positionTXS
            hitDistance,                    // iterationDistance
            1 / hit.linearDepth,            // 1 / sampled depth
            debug);
        FillScreenSpaceRaymarchingPostLoopDebug(
            1,                              // maxUsedLevel
            1,                              // iteration
            float3(0, 0, 0),                // rayTXS
            hit,
            debug);
        _DebugScreenSpaceTracingData[0] = debug;
    }
#endif


    return true;
}

// -------------------------------------------------
// Algorithm: HiZ raymarching
// -------------------------------------------------

// Based on Yasin Uludag, 2014. "Hi-Z Screen-Space Cone-Traced Reflections", GPU Pro5: Advanced Rendering Techniques
// Based on 2017. "Autodesk Gamedev | Notes On Screen Space HiZ Tracing", https://gamedev.autodesk.com/blogs/1/post/5866685274515295601

struct ScreenSpaceHiZRaymarchInput
{
    float3 rayOriginVS;         // Ray origin (VS)
    float3 rayDirVS;            // Ray direction (VS)
    float4x4 projectionMatrix;  // Projection matrix of the camera

#ifdef DEBUG_DISPLAY
    bool writeStepDebug;
#endif
};

bool ScreenSpaceHiZRaymarch(
    ScreenSpaceHiZRaymarchInput input,
    out ScreenSpaceRayHit hit)
{
    const float2 CROSS_OFFSET = float2(1, 1);
    const int MAX_ITERATIONS = 32;

    // Initialize loop
    ZERO_INITIALIZE(ScreenSpaceRayHit, hit);
    bool hitSuccessful = true;
    int iteration = 0;
    int minMipLevel = max(_SSRayMinLevel, 0);
    int maxMipLevel = min(_SSRayMaxLevel, int(_PyramidDepthMipSize.z));
    uint2 bufferSize = uint2(_PyramidDepthMipSize.xy);

    float3 startPositionTXS;
    float3 rayTXS;
    CalculateRayTXS(
        input.rayOriginVS,
        input.rayDirVS,
        input.projectionMatrix,
        bufferSize,
        startPositionTXS,
        rayTXS);

#ifdef DEBUG_DISPLAY
    int maxUsedLevel = minMipLevel;
    ScreenSpaceTracingDebug debug;
    ZERO_INITIALIZE(ScreenSpaceTracingDebug, debug);
    FillScreenSpaceRaymarchingPreLoopDebug(startPositionTXS, debug);
#endif

    {
        // Initialize raymarching
        float2 invRayTXS = float2(1, 1) / rayTXS.xy;

        // Calculate planes to intersect for each cell
        int2 cellPlanes = sign(rayTXS.xy);
        float2 crossOffset = CROSS_OFFSET * cellPlanes;
        cellPlanes = clamp(cellPlanes, 0, 1);

        int currentLevel = minMipLevel;
        uint2 cellCount = bufferSize >> currentLevel;
        uint2 cellSize = uint2(1, 1) << currentLevel;

        float3 positionTXS = startPositionTXS;

        while (currentLevel >= minMipLevel)
        {
            if (iteration >= MAX_ITERATIONS)
            {
                hitSuccessful = false;
                break;
            }

            cellCount = bufferSize >> currentLevel;
            cellSize = uint2(1, 1) << currentLevel;

#ifdef DEBUG_DISPLAY
            FillScreenSpaceRaymarchingPreIterationDebug(iteration, currentLevel, debug);
#endif

            // Go down in HiZ levels by default
            int mipLevelDelta = -1;

            // Sampled as 1/Z so it interpolate properly in screen space.
            const float invHiZDepth = LoadInvDepth(positionTXS.xy, currentLevel);
            float iterationDistance = 0;

            if (IsPositionAboveDepth(positionTXS.z, invHiZDepth))
            {
                float3 candidatePositionTXS = IntersectDepthPlane(positionTXS, rayTXS, invHiZDepth, iterationDistance);

                const int2 cellId = int2(positionTXS.xy) / cellSize;
                const int2 candidateCellId = int2(candidatePositionTXS.xy) / cellSize;

                // If we crossed the current cell
                if (!CellAreEquals(cellId, candidateCellId))
                {
                    candidatePositionTXS = IntersectCellPlanes(
                        positionTXS,
                        rayTXS,
                        invRayTXS,
                        cellId,
                        cellSize,
                        cellPlanes,
                        crossOffset,
                        iterationDistance);

                    // Go up a level to go faster
                    mipLevelDelta = 1;
                }

                positionTXS = candidatePositionTXS;
            }

            hit.distance += iterationDistance;

            currentLevel = min(currentLevel + mipLevelDelta, maxMipLevel);
            
#ifdef DEBUG_DISPLAY
            maxUsedLevel = max(maxUsedLevel, currentLevel);
            FillScreenSpaceRaymarchingPostIterationDebug(
                iteration,
                cellSize,
                positionTXS,
                iterationDistance,
                invHiZDepth,
                debug);
#endif

            // Check if we are out of the buffer
            if (any(int2(positionTXS.xy) > bufferSize)
                || any(positionTXS.xy < 0))
            {
                hitSuccessful = false;
                break;
            }

            ++iteration;
        }

        hit.linearDepth = 1 / positionTXS.z;
        hit.positionNDC = float2(positionTXS.xy) / float2(bufferSize);
        hit.positionSS = uint2(positionTXS.xy);
    }
    
#ifdef DEBUG_DISPLAY
    FillScreenSpaceRaymarchingPostLoopDebug(
        maxUsedLevel,
        iteration,
        rayTXS,
        hit,
        debug);
    FillScreenSpaceRaymarchingHitDebug(
        bufferSize, input.rayDirVS, rayTXS, startPositionTXS, hitSuccessful, iteration, MAX_ITERATIONS, maxMipLevel, maxUsedLevel,
        hit);
    if (input.writeStepDebug)
        _DebugScreenSpaceTracingData[0] = debug;
#endif

    return hitSuccessful;
}

// -------------------------------------------------
// Algorithm: Linear raymarching
// -------------------------------------------------
// Based on DDA (https://en.wikipedia.org/wiki/Digital_differential_analyzer_(graphics_algorithm))
// Based on Morgan McGuire and Michael Mara, 2014. "Efficient GPU Screen-Space Ray Tracing", Journal of Computer Graphics Techniques (JCGT), 235-256

struct ScreenSpaceLinearRaymarchInput
{
    float3 rayOriginVS;         // Ray origin (VS)
    float3 rayDirVS;            // Ray direction (VS)
    float4x4 projectionMatrix;  // Projection matrix of the camera

#ifdef DEBUG_DISPLAY
    bool writeStepDebug;
#endif
};

// Basically, perform a raycast with DDA technique on a specific mip level of the Depth pyramid.
bool ScreenSpaceLinearRaymarch(
    ScreenSpaceLinearRaymarchInput input,
    out ScreenSpaceRayHit hit)
{
    const float2 CROSS_OFFSET = float2(1, 1);
    const int MAX_ITERATIONS = 1024;

    // Initialize loop
    ZERO_INITIALIZE(ScreenSpaceRayHit, hit);
    bool hitSuccessful = true;
    int iteration = 0;
    int level = clamp(_SSRayMinLevel, 0, int(_PyramidDepthMipSize.z));
    uint2 bufferSize = uint2(_PyramidDepthMipSize.xy);

    float3 startPositionTXS;
    float3 rayTXS;
    CalculateRayTXS(
        input.rayOriginVS,
        input.rayDirVS,
        input.projectionMatrix,
        bufferSize,
        startPositionTXS,
        rayTXS);

#ifdef DEBUG_DISPLAY
    ScreenSpaceTracingDebug debug;
    ZERO_INITIALIZE(ScreenSpaceTracingDebug, debug);
    FillScreenSpaceRaymarchingPreLoopDebug(startPositionTXS, debug);
#endif

    float maxAbsAxis = max(abs(rayTXS.x), abs(rayTXS.y));
    // No need to raymarch if the ray is along camera's foward
    if (maxAbsAxis < 1E-7)
    {
        hit.distance = 1 / startPositionTXS.z;
        hit.linearDepth = 1 / startPositionTXS.z;
        hit.positionSS = uint2(startPositionTXS.xy);
    }
    else
    {
        // DDA step
        rayTXS /= max(abs(rayTXS.x), abs(rayTXS.y));
        rayTXS *= _SSRayMinLevel;

        float3 positionTXS = startPositionTXS;
        // TODO: We should have a for loop from the starting point to the far/near plane
        while (iteration < MAX_ITERATIONS)
        {
#ifdef DEBUG_DISPLAY
            FillScreenSpaceRaymarchingPreIterationDebug(iteration, 0, debug);
#endif

            positionTXS += rayTXS;
            float invHiZDepth = LoadInvDepth(positionTXS.xy, _SSRayMinLevel);

#ifdef DEBUG_DISPLAY
            FillScreenSpaceRaymarchingPostIterationDebug(
                iteration,
                uint2(0, 0),
                positionTXS,
                1 / rayTXS.z,
                invHiZDepth,
                debug);
#endif

            if (!IsPositionAboveDepth(positionTXS.z, invHiZDepth))
            {
                hitSuccessful = true;
                break;
            }

            // Check if we are out of the buffer
            if (any(int2(positionTXS.xy) > bufferSize)
                || any(positionTXS.xy < 0))
            {
                hitSuccessful = false;
                break;
            }

            ++iteration;
        }

        hit.linearDepth = 1 / positionTXS.z;
        hit.positionNDC = float2(positionTXS.xy) / float2(bufferSize);
        hit.positionSS = uint2(positionTXS.xy);
    }

#ifdef DEBUG_DISPLAY
    FillScreenSpaceRaymarchingPostLoopDebug(
        0,
        iteration,
        rayTXS,
        hit,
        debug);
    FillScreenSpaceRaymarchingHitDebug(
        bufferSize, input.rayDirVS, rayTXS, startPositionTXS, hitSuccessful, iteration, MAX_ITERATIONS, 0, 0,
        hit);
    if (input.writeStepDebug)
        _DebugScreenSpaceTracingData[0] = debug;
#endif

    return hitSuccessful;
}

#endif
