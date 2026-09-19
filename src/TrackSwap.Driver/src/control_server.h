#pragma once

#include <atomic>
#include <thread>

namespace trackswap
{
class TrackerRegistry;

class ControlServer final
{
public:
    ControlServer() = default;
    ~ControlServer();

    ControlServer(const ControlServer&) = delete;
    ControlServer& operator=(const ControlServer&) = delete;

    bool Start(TrackerRegistry* registry);
    void Stop();

private:
    void Run();

    std::atomic<bool> stopping_{false};
    TrackerRegistry* registry_ = nullptr;
    std::thread worker_;
};
} // namespace trackswap
