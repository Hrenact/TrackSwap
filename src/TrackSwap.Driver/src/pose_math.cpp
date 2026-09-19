#include "pose_math.h"

#include <cmath>
#include <cstddef>

namespace
{
vr::HmdQuaternion_t IdentityQuaternion()
{
    vr::HmdQuaternion_t result{};
    result.w = 1.0;
    return result;
}

vr::HmdQuaternion_t QuaternionFromMatrix(const vr::HmdMatrix34_t& matrix)
{
    vr::HmdQuaternion_t result{};
    const double trace = matrix.m[0][0] + matrix.m[1][1] + matrix.m[2][2];
    if (trace > 0.0)
    {
        const double scale = std::sqrt(trace + 1.0) * 2.0;
        result.w = 0.25 * scale;
        result.x = (matrix.m[2][1] - matrix.m[1][2]) / scale;
        result.y = (matrix.m[0][2] - matrix.m[2][0]) / scale;
        result.z = (matrix.m[1][0] - matrix.m[0][1]) / scale;
    }
    else if (matrix.m[0][0] > matrix.m[1][1] && matrix.m[0][0] > matrix.m[2][2])
    {
        const double scale = std::sqrt(1.0 + matrix.m[0][0] - matrix.m[1][1] - matrix.m[2][2]) * 2.0;
        result.w = (matrix.m[2][1] - matrix.m[1][2]) / scale;
        result.x = 0.25 * scale;
        result.y = (matrix.m[0][1] + matrix.m[1][0]) / scale;
        result.z = (matrix.m[0][2] + matrix.m[2][0]) / scale;
    }
    else if (matrix.m[1][1] > matrix.m[2][2])
    {
        const double scale = std::sqrt(1.0 + matrix.m[1][1] - matrix.m[0][0] - matrix.m[2][2]) * 2.0;
        result.w = (matrix.m[0][2] - matrix.m[2][0]) / scale;
        result.x = (matrix.m[0][1] + matrix.m[1][0]) / scale;
        result.y = 0.25 * scale;
        result.z = (matrix.m[1][2] + matrix.m[2][1]) / scale;
    }
    else
    {
        const double scale = std::sqrt(1.0 + matrix.m[2][2] - matrix.m[0][0] - matrix.m[1][1]) * 2.0;
        result.w = (matrix.m[1][0] - matrix.m[0][1]) / scale;
        result.x = (matrix.m[0][2] + matrix.m[2][0]) / scale;
        result.y = (matrix.m[1][2] + matrix.m[2][1]) / scale;
        result.z = 0.25 * scale;
    }

    const double length = std::sqrt(
        (result.w * result.w) + (result.x * result.x) +
        (result.y * result.y) + (result.z * result.z));
    if (length <= 0.0 || !std::isfinite(length))
    {
        return IdentityQuaternion();
    }

    result.w /= length;
    result.x /= length;
    result.y /= length;
    result.z /= length;
    return result;
}

vr::HmdQuaternion_t Normalize(const vr::HmdQuaternion_t& quaternion)
{
    const double length = std::sqrt(
        (quaternion.w * quaternion.w) + (quaternion.x * quaternion.x) +
        (quaternion.y * quaternion.y) + (quaternion.z * quaternion.z));
    if (length <= 0.0 || !std::isfinite(length))
    {
        return IdentityQuaternion();
    }

    return {
        quaternion.w / length,
        quaternion.x / length,
        quaternion.y / length,
        quaternion.z / length};
}

vr::HmdQuaternion_t Multiply(const vr::HmdQuaternion_t& left, const vr::HmdQuaternion_t& right)
{
    return Normalize({
        (left.w * right.w) - (left.x * right.x) - (left.y * right.y) - (left.z * right.z),
        (left.w * right.x) + (left.x * right.w) + (left.y * right.z) - (left.z * right.y),
        (left.w * right.y) - (left.x * right.z) + (left.y * right.w) + (left.z * right.x),
        (left.w * right.z) + (left.x * right.y) - (left.y * right.x) + (left.z * right.w)});
}

void RotateVector(const vr::HmdQuaternion_t& rotation, const double input[3], double output[3])
{
    const vr::HmdQuaternion_t q = Normalize(rotation);
    const double tx = 2.0 * ((q.y * input[2]) - (q.z * input[1]));
    const double ty = 2.0 * ((q.z * input[0]) - (q.x * input[2]));
    const double tz = 2.0 * ((q.x * input[1]) - (q.y * input[0]));
    output[0] = input[0] + (q.w * tx) + ((q.y * tz) - (q.z * ty));
    output[1] = input[1] + (q.w * ty) + ((q.z * tx) - (q.x * tz));
    output[2] = input[2] + (q.w * tz) + ((q.x * ty) - (q.y * tx));
}
} // namespace

namespace trackswap::pose_math
{
RigidOffset IdentityOffset()
{
    return {};
}

bool IsValidOffset(const RigidOffset& offset)
{
    constexpr double MaximumTranslationMetres = 10.0;
    for (double value : offset.translation)
    {
        if (!std::isfinite(value) || std::abs(value) > MaximumTranslationMetres)
        {
            return false;
        }
    }

    const double quaternionLengthSquared =
        (offset.rotation.w * offset.rotation.w) +
        (offset.rotation.x * offset.rotation.x) +
        (offset.rotation.y * offset.rotation.y) +
        (offset.rotation.z * offset.rotation.z);
    return std::isfinite(offset.rotation.w) &&
        std::isfinite(offset.rotation.x) &&
        std::isfinite(offset.rotation.y) &&
        std::isfinite(offset.rotation.z) &&
        quaternionLengthSquared >= 1e-12;
}

vr::DriverPose_t MakeInvalidPose()
{
    vr::DriverPose_t pose{};
    pose.qWorldFromDriverRotation = IdentityQuaternion();
    pose.qDriverFromHeadRotation = IdentityQuaternion();
    pose.qRotation = IdentityQuaternion();
    pose.result = vr::TrackingResult_Uninitialized;
    pose.poseIsValid = false;
    pose.deviceIsConnected = false;
    return pose;
}

vr::DriverPose_t ConvertPose(const vr::TrackedDevicePose_t& sourcePose)
{
    if (!sourcePose.bDeviceIsConnected || !sourcePose.bPoseIsValid)
    {
        vr::DriverPose_t invalid = MakeInvalidPose();
        invalid.deviceIsConnected = sourcePose.bDeviceIsConnected;
        invalid.result = sourcePose.eTrackingResult;
        return invalid;
    }

    vr::DriverPose_t pose{};
    pose.qWorldFromDriverRotation = IdentityQuaternion();
    pose.qDriverFromHeadRotation = IdentityQuaternion();
    pose.qRotation = QuaternionFromMatrix(sourcePose.mDeviceToAbsoluteTracking);
    for (std::size_t axis = 0; axis < 3; ++axis)
    {
        pose.vecPosition[axis] = sourcePose.mDeviceToAbsoluteTracking.m[axis][3];
        pose.vecVelocity[axis] = sourcePose.vVelocity.v[axis];
        pose.vecAngularVelocity[axis] = sourcePose.vAngularVelocity.v[axis];
    }

    pose.result = sourcePose.eTrackingResult;
    pose.poseIsValid = sourcePose.bPoseIsValid;
    pose.deviceIsConnected = sourcePose.bDeviceIsConnected;
    return pose;
}

vr::DriverPose_t ApplyOffset(const vr::DriverPose_t& sourcePose, const RigidOffset& offset)
{
    if (!sourcePose.deviceIsConnected || !sourcePose.poseIsValid || !IsValidOffset(offset))
    {
        return sourcePose;
    }

    vr::DriverPose_t output = sourcePose;
    double rotatedTranslation[3]{};
    RotateVector(sourcePose.qRotation, offset.translation, rotatedTranslation);
    for (std::size_t axis = 0; axis < 3; ++axis)
    {
        output.vecPosition[axis] += rotatedTranslation[axis];
    }

    output.qRotation = Multiply(sourcePose.qRotation, offset.rotation);
    output.vecVelocity[0] +=
        (sourcePose.vecAngularVelocity[1] * rotatedTranslation[2]) -
        (sourcePose.vecAngularVelocity[2] * rotatedTranslation[1]);
    output.vecVelocity[1] +=
        (sourcePose.vecAngularVelocity[2] * rotatedTranslation[0]) -
        (sourcePose.vecAngularVelocity[0] * rotatedTranslation[2]);
    output.vecVelocity[2] +=
        (sourcePose.vecAngularVelocity[0] * rotatedTranslation[1]) -
        (sourcePose.vecAngularVelocity[1] * rotatedTranslation[0]);
    return output;
}
} // namespace trackswap::pose_math
