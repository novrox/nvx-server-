(function () {
    if (!document.body || document.body.getAttribute("data-poll") !== "status") {
        return;
    }

    var lampClass = {
        running: "on",
        blocked: "busy",
        stopped: "off",
        missing: "off",
        disabled: "off"
    };

    function apply(data) {
        if (!data || !data.services) {
            return;
        }
        Object.keys(data.services).forEach(function (id) {
            var service = data.services[id];
            document.querySelectorAll('[data-state="' + id + '"]').forEach(function (node) {
                node.textContent = service.label;
            });
            document.querySelectorAll('[data-detail="' + id + '"]').forEach(function (node) {
                node.textContent = "Port " + service.port + " · " + service.detail;
            });
            document.querySelectorAll('[data-lamp="' + id + '"]').forEach(function (node) {
                node.className = "lamp " + (lampClass[service.state] || "off");
            });
            if (id === "php") {
                document.querySelectorAll('[data-uptime="php"]').forEach(function (node) {
                    node.textContent = service.state === "running" ? formatUptime(service.uptime) : "Down";
                });
            }
        });
        if (data.url) {
            document.querySelectorAll("[data-url]").forEach(function (node) {
                node.textContent = data.url;
            });
        }
    }

    function formatUptime(seconds) {
        seconds = Number(seconds) || 0;
        if (seconds < 60) return seconds + "s";
        var minutes = Math.floor(seconds / 60);
        if (minutes < 60) return minutes + "m";
        return Math.floor(minutes / 60) + "h " + (minutes % 60) + "m";
    }

    function tick() {
        fetch("/api/status.php", { headers: { "Accept": "application/json" } })
            .then(function (response) {
                if (!response.ok) throw new Error("status");
                return response.json();
            })
            .then(function (data) {
                if (!data || !data.services) throw new Error("status");
                apply(data);
                document.querySelectorAll("[data-connection]").forEach(function (node) {
                    node.textContent = "Server connected";
                    node.setAttribute("data-state", "connected");
                });
            })
            .catch(function () {
                document.querySelectorAll("[data-connection]").forEach(function (node) {
                    node.textContent = "Server connection lost - retrying";
                    node.setAttribute("data-state", "disconnected");
                });
            });
    }

    tick();
    setInterval(tick, 4000);
})();
