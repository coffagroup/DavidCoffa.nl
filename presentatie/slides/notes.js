window.addEventListener("message", (event) => {
  if (event.data?.type === "notes") {
    document.body.classList.toggle("show-notes", Boolean(event.data.show));
  }
});
