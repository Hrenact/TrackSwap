#pragma once

#include <atomic>
#include <thread>

namespace trackswap
{
class VirtualTracker;

class ControlServer final
{
public:
    ControlServer() = default;
    ~ControlServer();

    ControlServer(const ControlServer&) = delete;
    ControlServer& operator=(const ControlServer&) = delete;

    bool Start(VirtualTracker* tracker);
    void Stop();

private:
    void Run();

    std::atomic<bool> stopping_{false};
    VirtualTracker* tracker_ = nullptr;
    std::thread worker_;
};
} // namespace trackswap
