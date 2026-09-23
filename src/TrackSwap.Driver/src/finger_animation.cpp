#include "finger_animation.h"

#include <algorithm>
#include <cmath>

namespace
{
constexpr float ResponseSeconds = 0.075F;
constexpr float MaximumStepSeconds = 0.1F;

float Clamp01(float value)
{
    return std::clamp(value, 0.0F, 1.0F);
}

float AdvanceValue(float current, float target, float elapsedSeconds)
{
    const float boundedElapsed = std::clamp(elapsedSeconds, 0.0F, MaximumStepSeconds);
    const float alpha = 1.0F - std::exp(-boundedElapsed / ResponseSeconds);
    return current + (target - current) * alpha;
}
}

namespace trackswap::finger_animation
{
HandAnimationState ComputeTargets(const control_protocol::ControllerInputState& input)
{
    const float stickActivity = Clamp01(std::sqrt(
        input.joystickX * input.joystickX + input.joystickY * input.joystickY));
    float thumbCurl = 0.0F;
    float thumbSplay = 0.0F;
    if (input.secondaryButton != 0)
    {
        thumbCurl = 0.35F;
        thumbSplay = 0.45F;
    }
    else if (input.primaryButton != 0)
    {
        thumbCurl = 0.5F;
        thumbSplay = 0.05F;
    }
    else if (input.joystickClick != 0 || stickActivity > 0.05F)
    {
        thumbCurl = 0.3F + 0.1F * Clamp01(-input.joystickY);
        thumbSplay = -0.45F + 0.1F * std::clamp(input.joystickX, -1.0F, 1.0F);
    }
    else if (input.menuButton != 0)
    {
        thumbCurl = 0.75F;
        thumbSplay = 0.15F;
    }
    const float index = input.triggerValue > 0.0F
        ? Clamp01(input.triggerValue)
        : (input.triggerClick != 0 ? 1.0F : 0.0F);
    const float grip = input.gripValue > 0.0F
        ? Clamp01(input.gripValue)
        : (input.gripClick != 0 ? 1.0F : 0.0F);
    return {
        { thumbCurl, index, grip, grip, grip },
        {
            thumbSplay,
            0.32F * (1.0F - index) - 0.2F * index,
            0.08F * (1.0F - grip),
            -0.12F * (1.0F - grip) + 0.07F * grip,
            -0.3F * (1.0F - grip) + 0.13F * grip
        }
    };
}

void Advance(HandAnimationState& current, const HandAnimationState& target, float elapsedSeconds)
{
    current.curls.thumb = AdvanceValue(current.curls.thumb, target.curls.thumb, elapsedSeconds);
    current.curls.index = AdvanceValue(current.curls.index, target.curls.index, elapsedSeconds);
    current.curls.middle = AdvanceValue(current.curls.middle, target.curls.middle, elapsedSeconds);
    current.curls.ring = AdvanceValue(current.curls.ring, target.curls.ring, elapsedSeconds);
    current.curls.pinky = AdvanceValue(current.curls.pinky, target.curls.pinky, elapsedSeconds);
    current.splays.thumb = AdvanceValue(current.splays.thumb, target.splays.thumb, elapsedSeconds);
    current.splays.index = AdvanceValue(current.splays.index, target.splays.index, elapsedSeconds);
    current.splays.middle = AdvanceValue(current.splays.middle, target.splays.middle, elapsedSeconds);
    current.splays.ring = AdvanceValue(current.splays.ring, target.splays.ring, elapsedSeconds);
    current.splays.pinky = AdvanceValue(current.splays.pinky, target.splays.pinky, elapsedSeconds);
}
}
