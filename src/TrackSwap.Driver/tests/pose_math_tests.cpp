#include "pose_math.h"

#include <cmath>
#include <cstdlib>

namespace
{
constexpr double Tolerance = 1e-6;

bool Near(double left, double right)
{
    return std::abs(left - right) <= Tolerance;
}
} // namespace

int main()
{
    const vr::DriverPose_t unavailable = trackswap::pose_math::MakeInvalidPose(true);
    if (unavailable.poseIsValid || !unavailable.deviceIsConnected ||
        unavailable.result != vr::TrackingResult_Uninitialized ||
        !Near(unavailable.qWorldFromDriverRotation.w, 1.0) ||
        !Near(unavailable.qDriverFromHeadRotation.w, 1.0))
    {
        return EXIT_FAILURE;
    }

    vr::TrackedDevicePose_t source{};
    source.bDeviceIsConnected = true;
    source.bPoseIsValid = true;
    source.eTrackingResult = vr::TrackingResult_Running_OK;
    source.mDeviceToAbsoluteTracking.m[0][0] = 1.0F;
    source.mDeviceToAbsoluteTracking.m[1][1] = 1.0F;
    source.mDeviceToAbsoluteTracking.m[2][2] = 1.0F;
    source.mDeviceToAbsoluteTracking.m[0][3] = 1.25F;
    source.mDeviceToAbsoluteTracking.m[1][3] = -2.5F;
    source.mDeviceToAbsoluteTracking.m[2][3] = 3.75F;
    source.vVelocity.v[0] = 4.0F;
    source.vAngularVelocity.v[2] = 0.5F;

    const vr::DriverPose_t converted = trackswap::pose_math::ConvertPose(source);
    if (!converted.poseIsValid || !converted.deviceIsConnected ||
        converted.result != vr::TrackingResult_Running_OK ||
        !Near(converted.qRotation.w, 1.0) ||
        !Near(converted.qRotation.x, 0.0) ||
        !Near(converted.vecPosition[0], 1.25) ||
        !Near(converted.vecPosition[1], -2.5) ||
        !Near(converted.vecPosition[2], 3.75) ||
        !Near(converted.vecVelocity[0], 4.0) ||
        !Near(converted.vecAngularVelocity[2], 0.5))
    {
        return EXIT_FAILURE;
    }

    source.bPoseIsValid = false;
    const vr::DriverPose_t invalid = trackswap::pose_math::ConvertPose(source);
    if (invalid.poseIsValid || !invalid.deviceIsConnected ||
        !Near(invalid.qWorldFromDriverRotation.w, 1.0) ||
        !Near(invalid.qDriverFromHeadRotation.w, 1.0))
    {
        return EXIT_FAILURE;
    }

    vr::DriverPose_t offsetSource = converted;
    offsetSource.vecPosition[0] = 0.0;
    offsetSource.vecPosition[1] = 0.0;
    offsetSource.vecPosition[2] = 0.0;
    offsetSource.vecVelocity[0] = 0.0;
    offsetSource.vecVelocity[1] = 0.0;
    offsetSource.vecVelocity[2] = 0.0;
    offsetSource.vecAngularVelocity[0] = 0.0;
    offsetSource.vecAngularVelocity[1] = 0.0;
    offsetSource.vecAngularVelocity[2] = 2.0;

    const double halfSqrtTwo = std::sqrt(0.5);
    const trackswap::pose_math::RigidOffset offset{
        {1.0, 0.0, 0.0},
        {halfSqrtTwo, 0.0, halfSqrtTwo, 0.0}};
    const vr::DriverPose_t offsetOutput = trackswap::pose_math::ApplyOffset(offsetSource, offset);
    if (!Near(offsetOutput.vecPosition[0], 1.0) ||
        !Near(offsetOutput.vecPosition[1], 0.0) ||
        !Near(offsetOutput.qRotation.w, halfSqrtTwo) ||
        !Near(offsetOutput.qRotation.y, halfSqrtTwo) ||
        !Near(offsetOutput.vecVelocity[0], 0.0) ||
        !Near(offsetOutput.vecVelocity[1], 2.0) ||
        !Near(offsetOutput.vecVelocity[2], 0.0))
    {
        return EXIT_FAILURE;
    }

    const trackswap::pose_math::RigidOffset coordinateDirectionOffset{
        {1.0, 0.0, 1.0},
        {1.0, 0.0, 0.0, 0.0}};
    const vr::DriverPose_t coordinateDirectionOutput =
        trackswap::pose_math::ApplyOffset(offsetSource, coordinateDirectionOffset);
    if (!Near(coordinateDirectionOutput.vecPosition[0], 1.0) ||
        !Near(coordinateDirectionOutput.vecPosition[1], 0.0) ||
        !Near(coordinateDirectionOutput.vecPosition[2], 1.0))
    {
        return EXIT_FAILURE;
    }

    offsetSource.qRotation = {halfSqrtTwo, 0.0, 0.0, halfSqrtTwo};
    const trackswap::pose_math::RigidOffset localTranslation{
        {1.0, 0.0, 0.0},
        {1.0, 0.0, 0.0, 0.0}};
    const vr::DriverPose_t rotatedTranslation =
        trackswap::pose_math::ApplyOffset(offsetSource, localTranslation);
    if (!Near(rotatedTranslation.vecPosition[0], 0.0) ||
        !Near(rotatedTranslation.vecPosition[1], 1.0))
    {
        return EXIT_FAILURE;
    }

    trackswap::pose_math::RigidOffset excessive = trackswap::pose_math::IdentityOffset();
    excessive.translation[0] = 10.01;
    if (trackswap::pose_math::IsValidOffset(excessive))
    {
        return EXIT_FAILURE;
    }

    return EXIT_SUCCESS;
}
