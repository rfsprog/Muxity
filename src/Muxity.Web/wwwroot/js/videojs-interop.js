/**
 * Video.js interop for Blazor WASM.
 * Requires Video.js + videojs-http-streaming loaded before this script.
 */
window.muxityPlayer = {
    _players: {},

    init: function (elementId, src, poster) {
        if (this._players[elementId]) return;

        const options = {
            controls:    true,
            fluid:       true,
            playbackRates: [0.5, 1, 1.25, 1.5, 2],
            sources:     [{ src, type: 'application/x-mpegURL' }],
        };
        if (poster) options.poster = poster;

        const player = videojs(elementId, options);

        // Quality selector using built-in VHS representations
        player.ready(function () {
            const vhs = player.tech({ IWillNotUseThisInPlugins: true })?.vhs;
            if (!vhs) return;

            vhs.representations().forEach(rep => {
                console.log(`Quality: ${rep.height}p`);
            });
        });

        this._players[elementId] = player;
    },

    dispose: function (elementId) {
        const player = this._players[elementId];
        if (player) {
            player.dispose();
            delete this._players[elementId];
        }
    }
};
