#pragma once

#include "control_protocol.h"

namespace trackswap::finger_animation
{
struct FingerCurls
{
    float thumb = 0.0F;
    float index = 0.0F;
    float middle = 0.0F;
    float ring = 0.0F;
    float pinky = 0.0F;
};

struct FingerSplays
{
    float thumb = 0.0F;
    float index = 0.0F;
    float middle = 0.0F;
    float ring = 0.0F;
    float pinky = 0.0F;
};

struct HandAnimationState
{
    FingerCurls curls;
    FingerSplays splays;
};

HandAnimationState ComputeTargets(const control_protocol::ControllerInputState& input);
void Advance(HandAnimationState& current, const HandAnimationState& target, float elapsedSeconds);
}
