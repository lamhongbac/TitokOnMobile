// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

function getTiktokPlayerUrl(videoId, autoPlayEnabled) {
    const autoplayParam = autoPlayEnabled ? "&autoplay=1&mute=0" : "";
    return `https://www.tiktok.com/player/v1/${videoId}?music_info=1&description=1${autoplayParam}`;
}
