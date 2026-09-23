#include "finger_animation.h"
#include "controller_input_routing.h"

#include <cmath>
#include <cstdlib>

namespace
{
bool Near(float left, float right, float tolerance = 0.0001F)
{
    return std::fabs(left - right) <= tolerance;
}
}

int main()
{
    using trackswap::control_protocol::ControllerInputState;
    using trackswap::finger_animation::Advance;
    using trackswap::finger_animation::ComputeTargets;
    using trackswap::finger_animation::HandAnimationState;
    using trackswap::controller_input_routing::BuildLeftInput;
    using trackswap::controller_input_routing::BuildRightInput;

    ControllerInputState input{};
    input.primaryButton = 1;
    input.triggerValue = 0.4F;
    input.gripValue = 0.7F;
    HandAnimationState targets = ComputeTargets(input);
    if (!Near(targets.curls.thumb, 0.5F) || !Near(targets.curls.index, 0.4F) ||
        !Near(targets.curls.middle, 0.7F) || !Near(targets.curls.ring, 0.7F) || !Near(targets.curls.pinky, 0.7F))
    {
        return EXIT_FAILURE;
    }

    input = {};
    input.joystickX = 0.6F;
    input.joystickY = 0.8F;
    targets = ComputeTargets(input);
    if (!Near(targets.curls.thumb, 0.3F) || !Near(targets.splays.thumb, -0.39F)) return EXIT_FAILURE;

    input = {};
    input.triggerValue = 1.0F;
    input.gripValue = 1.0F;
    targets = ComputeTargets(input);
    if (!Near(targets.curls.index, 1.0F) || !Near(targets.curls.middle, 1.0F)) return EXIT_FAILURE;

    input.triggerValue = 0.25F;
    input.gripValue = 0.5F;
    targets = ComputeTargets(input);
    if (!Near(targets.curls.index, 0.25F) || !Near(targets.curls.middle, 0.5F)) return EXIT_FAILURE;

    input = {};
    input.secondaryButton = 1;
    const HandAnimationState secondary = ComputeTargets(input);
    input = {};
    input.primaryButton = 1;
    const HandAnimationState primary = ComputeTargets(input);
    if (Near(secondary.splays.thumb, primary.splays.thumb) ||
        Near(secondary.curls.thumb, primary.curls.thumb)) return EXIT_FAILURE;

    input = {};
    input.hasExplicitTouchState = 1;
    input.joystickTouch = 1;
    input.triggerTouch = 1;
    const HandAnimationState assistedTouch = ComputeTargets(input);
    if (!Near(assistedTouch.curls.thumb, 0.3F) ||
        !Near(assistedTouch.splays.thumb, -0.45F) ||
        !Near(assistedTouch.curls.index, 0.12F)) return EXIT_FAILURE;

    input.joystickTouch = 0;
    input.triggerTouch = 0;
    const HandAnimationState assistedRelease = ComputeTargets(input);
    if (!Near(assistedRelease.curls.thumb, 0.0F) ||
        !Near(assistedRelease.curls.index, 0.0F)) return EXIT_FAILURE;

    input = {};
    const HandAnimationState open = ComputeTargets(input);
    input.gripValue = 1.0F;
    const HandAnimationState closed = ComputeTargets(input);
    if (!(open.splays.middle > closed.splays.middle) ||
        !(open.splays.pinky < closed.splays.pinky)) return EXIT_FAILURE;

    HandAnimationState current{};
    HandAnimationState target{};
    target.curls = { 1.0F, 1.0F, 1.0F, 1.0F, 1.0F };
    target.splays = { 0.5F, 0.5F, 0.5F, -0.5F, -0.5F };
    Advance(current, target, 0.01F);
    if (current.curls.thumb <= 0.0F || current.curls.thumb >= 1.0F) return EXIT_FAILURE;
    if (current.splays.thumb <= 0.0F || current.splays.thumb >= 0.5F) return EXIT_FAILURE;
    const float first = current.curls.thumb;
    Advance(current, target, 0.01F);
    if (current.curls.thumb <= first || current.curls.thumb >= 1.0F) return EXIT_FAILURE;

    ControllerInputState leftInput{};
    ControllerInputState rightInput{};
    leftInput.hand = 1;
    rightInput.hand = 2;
    rightInput.menuButton = 1;
    rightInput.hasExplicitTouchState = 1;
    rightInput.menuTouch = 1;
    ControllerInputState routedLeft = BuildLeftInput(leftInput, rightInput);
    ControllerInputState routedRight = BuildRightInput(rightInput);
    if (routedLeft.hand != 1 || routedLeft.menuButton == 0 || routedLeft.menuTouch == 0) return EXIT_FAILURE;
    if (routedRight.hand != 2 || routedRight.menuButton != 0 || routedRight.menuTouch != 0) return EXIT_FAILURE;

    leftInput.menuButton = 1;
    leftInput.hasExplicitTouchState = 1;
    leftInput.menuTouch = 1;
    rightInput.menuButton = 0;
    rightInput.menuTouch = 0;
    routedLeft = BuildLeftInput(leftInput, rightInput);
    if (routedLeft.menuButton == 0 || routedLeft.menuTouch == 0) return EXIT_FAILURE;

    leftInput.menuButton = 0;
    leftInput.menuTouch = 0;
    routedLeft = BuildLeftInput(leftInput, rightInput);
    if (routedLeft.menuButton != 0 || routedLeft.menuTouch != 0) return EXIT_FAILURE;
    return EXIT_SUCCESS;
}
