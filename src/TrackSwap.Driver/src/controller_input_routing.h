#pragma once

#include "control_protocol.h"

namespace trackswap::controller_input_routing
{
inline control_protocol::ControllerInputState BuildLeftInput(
    const control_protocol::ControllerInputState& left,
    const control_protocol::ControllerInputState& right)
{
    auto result = left;
    result.hand = 1;
    result.menuButton = (left.menuButton != 0 || right.menuButton != 0) ? 1U : 0U;
    const bool leftTouch = left.hasExplicitTouchState != 0
        ? left.menuTouch != 0
        : left.menuButton != 0;
    const bool rightTouch = right.hasExplicitTouchState != 0
        ? right.menuTouch != 0
        : right.menuButton != 0;
    result.menuTouch = (leftTouch || rightTouch) ? 1U : 0U;
    return result;
}

inline control_protocol::ControllerInputState BuildRightInput(
    const control_protocol::ControllerInputState& right)
{
    auto result = right;
    result.hand = 2;
    result.menuButton = 0;
    result.menuTouch = 0;
    return result;
}
} // namespace trackswap::controller_input_routing
