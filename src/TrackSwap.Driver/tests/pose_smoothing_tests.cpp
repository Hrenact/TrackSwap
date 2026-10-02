#include "pose_smoothing.h"

#include <cmath>
#include <cstdlib>

namespace
{
constexpr double Tolerance = 1e-6;

bool Near(double left, double right)
{
    return std::abs(left - right) <= Tolerance;
}

vr::DriverPose_t MakePose(double x, const vr::HmdQuaternion_t& rotation)
{
    vr::DriverPose_t pose{};
    pose.qWorldFromDriverRotation.w = 1.0;
    pose.qDriverFromHeadRotation.w = 1.0;
    pose.qRotation = rotation;
    pose.vecPosition[0] = x;
    pose.vecVelocity[0] = x;
    pose.vecAngularVelocity[1] = x;
    pose.result = vr::TrackingResult_Running_OK;
    pose.poseIsValid = true;
    pose.deviceIsConnected = true;
    return pose;
}
} // namespace

int main()
{
    using trackswap::pose_smoothing::Configuration;
    using trackswap::pose_smoothing::PoseSmoother;

    Configuration configuration{};
    if (!Near(configuration.positionStrength, 0.0) ||
        !Near(configuration.rotationStrength, 0.0))
    {
        return EXIT_FAILURE;
    }
    configuration.enabled = true;
    configuration.positionStrength = 100.0;
    configuration.rotationStrength = 100.0;
    PoseSmoother smoother;
    const vr::DriverPose_t first = smoother.ApplyForInterval(
        MakePose(0.0, {1.0, 0.0, 0.0, 0.0}), configuration, 1.0 / 90.0);
    const vr::DriverPose_t second = smoother.ApplyForInterval(
        MakePose(1.0, {0.0, 0.0, 1.0, 0.0}), configuration, 1.0 / 90.0);
    if (!Near(first.vecPosition[0], 0.0) || second.vecPosition[0] <= 0.0 ||
        second.vecPosition[0] >= 1.0 || second.vecVelocity[0] <= 0.0 ||
        second.vecVelocity[0] >= 1.0 || !Near(
            second.qRotation.w * second.qRotation.w +
                second.qRotation.x * second.qRotation.x +
                second.qRotation.y * second.qRotation.y +
                second.qRotation.z * second.qRotation.z,
            1.0))
    {
        return EXIT_FAILURE;
    }

    configuration.smoothPosition = false;
    const vr::DriverPose_t positionBypass = smoother.ApplyForInterval(
        MakePose(4.0, {0.0, 0.0, -1.0, 0.0}), configuration, 1.0 / 90.0);
    if (!Near(positionBypass.vecPosition[0], 4.0) || !Near(positionBypass.vecVelocity[0], 4.0))
    {
        return EXIT_FAILURE;
    }

    vr::DriverPose_t invalid = MakePose(8.0, {1.0, 0.0, 0.0, 0.0});
    invalid.poseIsValid = false;
    smoother.ApplyForInterval(invalid, configuration, 1.0 / 90.0);
    const vr::DriverPose_t recovered = smoother.ApplyForInterval(
        MakePose(8.0, {1.0, 0.0, 0.0, 0.0}), configuration, 1.0 / 90.0);
    if (!Near(recovered.vecPosition[0], 8.0))
    {
        return EXIT_FAILURE;
    }

    Configuration invalidConfiguration = configuration;
    invalidConfiguration.positionStrength = 101.0;
    if (trackswap::pose_smoothing::IsValidConfiguration(invalidConfiguration))
    {
        return EXIT_FAILURE;
    }

    return EXIT_SUCCESS;
}
