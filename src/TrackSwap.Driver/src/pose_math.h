#pragma once

#include <openvr_driver.h>

namespace trackswap::pose_math
{
struct RigidOffset
{
    double translation[3]{};
    vr::HmdQuaternion_t rotation{1.0, 0.0, 0.0, 0.0};
};

RigidOffset IdentityOffset();
bool IsValidOffset(const RigidOffset& offset);
vr::DriverPose_t MakeInvalidPose(bool deviceIsConnected = false);
vr::DriverPose_t ConvertPose(const vr::TrackedDevicePose_t& sourcePose);
vr::DriverPose_t ApplyOffset(const vr::DriverPose_t& sourcePose, const RigidOffset& offset);
} // namespace trackswap::pose_math
