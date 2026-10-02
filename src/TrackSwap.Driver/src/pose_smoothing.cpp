#include "pose_smoothing.h"

#include <algorithm>
#include <cmath>

namespace
{
constexpr double MaximumTimeConstantSeconds = 0.25;
constexpr double DefaultIntervalSeconds = 1.0 / 90.0;
constexpr double MinimumIntervalSeconds = 0.001;
constexpr double MaximumIntervalSeconds = 0.1;

double InterpolationFactor(double strength, double intervalSeconds)
{
    if (strength <= 0.0)
    {
        return 1.0;
    }

    const double normalized = strength / 100.0;
    const double timeConstant = MaximumTimeConstantSeconds * normalized * normalized;
    return 1.0 - std::exp(-intervalSeconds / timeConstant);
}

vr::HmdQuaternion_t Normalize(const vr::HmdQuaternion_t& value)
{
    const double length = std::sqrt(
        value.w * value.w + value.x * value.x + value.y * value.y + value.z * value.z);
    if (!std::isfinite(length) || length <= 1e-12)
    {
        return {1.0, 0.0, 0.0, 0.0};
    }
    return {value.w / length, value.x / length, value.y / length, value.z / length};
}

vr::HmdQuaternion_t SlerpShortest(
    const vr::HmdQuaternion_t& fromValue,
    const vr::HmdQuaternion_t& toValue,
    double amount)
{
    const vr::HmdQuaternion_t from = Normalize(fromValue);
    vr::HmdQuaternion_t to = Normalize(toValue);
    double dot = from.w * to.w + from.x * to.x + from.y * to.y + from.z * to.z;
    if (dot < 0.0)
    {
        dot = -dot;
        to = {-to.w, -to.x, -to.y, -to.z};
    }
    dot = std::clamp(dot, -1.0, 1.0);

    if (dot > 0.9995)
    {
        return Normalize({
            from.w + amount * (to.w - from.w),
            from.x + amount * (to.x - from.x),
            from.y + amount * (to.y - from.y),
            from.z + amount * (to.z - from.z)});
    }

    const double angle = std::acos(dot);
    const double sine = std::sin(angle);
    const double fromWeight = std::sin((1.0 - amount) * angle) / sine;
    const double toWeight = std::sin(amount * angle) / sine;
    return Normalize({
        from.w * fromWeight + to.w * toWeight,
        from.x * fromWeight + to.x * toWeight,
        from.y * fromWeight + to.y * toWeight,
        from.z * fromWeight + to.z * toWeight});
}
} // namespace

namespace trackswap::pose_smoothing
{
bool IsValidConfiguration(const Configuration& configuration)
{
    return (!configuration.enabled || configuration.smoothPosition || configuration.smoothRotation) &&
        std::isfinite(configuration.positionStrength) &&
        configuration.positionStrength >= 0.0 && configuration.positionStrength <= 100.0 &&
        std::isfinite(configuration.rotationStrength) &&
        configuration.rotationStrength >= 0.0 && configuration.rotationStrength <= 100.0;
}

vr::DriverPose_t PoseSmoother::Apply(
    const vr::DriverPose_t& pose,
    const Configuration& configuration)
{
    const auto now = std::chrono::steady_clock::now();
    double intervalSeconds = DefaultIntervalSeconds;
    if (hasLastUpdate_)
    {
        intervalSeconds = std::chrono::duration<double>(now - lastUpdate_).count();
    }
    lastUpdate_ = now;
    hasLastUpdate_ = true;
    return ApplyForInterval(pose, configuration, intervalSeconds);
}

vr::DriverPose_t PoseSmoother::ApplyForInterval(
    const vr::DriverPose_t& pose,
    const Configuration& configuration,
    double intervalSeconds)
{
    if (!pose.deviceIsConnected || !pose.poseIsValid ||
        !configuration.enabled || !IsValidConfiguration(configuration))
    {
        Reset();
        return pose;
    }

    if (!initialized_)
    {
        filteredPose_ = pose;
        initialized_ = true;
        return pose;
    }

    if (!std::isfinite(intervalSeconds) || intervalSeconds <= 0.0)
    {
        intervalSeconds = DefaultIntervalSeconds;
    }
    intervalSeconds = std::clamp(intervalSeconds, MinimumIntervalSeconds, MaximumIntervalSeconds);

    vr::DriverPose_t output = pose;
    if (configuration.smoothPosition)
    {
        const double amount = InterpolationFactor(configuration.positionStrength, intervalSeconds);
        for (std::size_t axis = 0; axis < 3; ++axis)
        {
            output.vecPosition[axis] = filteredPose_.vecPosition[axis] +
                amount * (pose.vecPosition[axis] - filteredPose_.vecPosition[axis]);
            output.vecVelocity[axis] = filteredPose_.vecVelocity[axis] +
                amount * (pose.vecVelocity[axis] - filteredPose_.vecVelocity[axis]);
        }
    }
    if (configuration.smoothRotation)
    {
        const double amount = InterpolationFactor(configuration.rotationStrength, intervalSeconds);
        output.qRotation = SlerpShortest(filteredPose_.qRotation, pose.qRotation, amount);
        for (std::size_t axis = 0; axis < 3; ++axis)
        {
            output.vecAngularVelocity[axis] = filteredPose_.vecAngularVelocity[axis] +
                amount * (pose.vecAngularVelocity[axis] - filteredPose_.vecAngularVelocity[axis]);
        }
    }

    filteredPose_ = output;
    return output;
}

void PoseSmoother::Reset()
{
    filteredPose_ = {};
    initialized_ = false;
    hasLastUpdate_ = false;
}
} // namespace trackswap::pose_smoothing
