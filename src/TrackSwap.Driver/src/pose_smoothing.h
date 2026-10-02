#pragma once

#include <chrono>

#include <openvr_driver.h>

namespace trackswap::pose_smoothing
{
struct Configuration
{
    bool enabled = false;
    bool smoothPosition = true;
    bool smoothRotation = true;
    double positionStrength = 0.0;
    double rotationStrength = 0.0;
};

bool IsValidConfiguration(const Configuration& configuration);

class PoseSmoother
{
public:
    vr::DriverPose_t Apply(const vr::DriverPose_t& pose, const Configuration& configuration);
    vr::DriverPose_t ApplyForInterval(
        const vr::DriverPose_t& pose,
        const Configuration& configuration,
        double intervalSeconds);
    void Reset();

private:
    vr::DriverPose_t filteredPose_{};
    std::chrono::steady_clock::time_point lastUpdate_{};
    bool initialized_ = false;
    bool hasLastUpdate_ = false;
};
} // namespace trackswap::pose_smoothing
