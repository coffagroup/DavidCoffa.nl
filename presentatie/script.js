const slideCount = 22;
const frame = document.getElementById("slideFrame");
const counter = document.getElementById("counter");
const progress = document.getElementById("progress");
const dots = document.getElementById("dots");
const previousButton = document.getElementById("previousButton");
const nextButton = document.getElementById("nextButton");
const homeButton = document.getElementById("homeButton");
const fullscreenButton = document.getElementById("fullscreenButton");
const notesButton = document.getElementById("notesButton");
const quickButtons = Array.from(document.querySelectorAll("[data-go]"));

let current = 0;
let showNotes = false;

function slidePath(index) {
  return `slides/slide-${String(index + 1).padStart(2, "0")}.html`;
}

function format(number) {
  return String(number).padStart(2, "0");
}

function setNotesInFrame() {
  frame.contentWindow?.postMessage({ type: "notes", show: showNotes }, "*");
}

function showSlide(index) {
  if (index < 0 || index >= slideCount) return;

  current = index;
  frame.src = slidePath(index);
  counter.textContent = `${format(current + 1)} / ${format(slideCount)}`;
  progress.style.width = `${((current + 1) / slideCount) * 100}%`;
  previousButton.disabled = current === 0;
  nextButton.disabled = current === slideCount - 1;

  Array.from(dots.children).forEach((dot, dotIndex) => {
    dot.classList.toggle("active", dotIndex === current);
  });

  quickButtons.forEach((button) => {
    button.classList.toggle("active", Number(button.dataset.go) === current);
  });
}

function nextSlide() { showSlide(current + 1); }
function previousSlide() { showSlide(current - 1); }

for (let index = 0; index < slideCount; index += 1) {
  const dot = document.createElement("button");
  dot.type = "button";
  dot.setAttribute("aria-label", `Ga naar slide ${index + 1}`);
  dot.addEventListener("click", () => showSlide(index));
  dots.appendChild(dot);
}

previousButton.addEventListener("click", previousSlide);
nextButton.addEventListener("click", nextSlide);
homeButton.addEventListener("click", () => showSlide(0));
quickButtons.forEach((button) => button.addEventListener("click", () => showSlide(Number(button.dataset.go))));

fullscreenButton.addEventListener("click", () => {
  if (!document.fullscreenElement) {
    document.documentElement.requestFullscreen();
  } else {
    document.exitFullscreen();
  }
});

notesButton.addEventListener("click", () => {
  showNotes = !showNotes;
  notesButton.classList.toggle("active", showNotes);
  setNotesInFrame();
});

frame.addEventListener("load", setNotesInFrame);

window.addEventListener("keydown", (event) => {
  if (["INPUT", "TEXTAREA", "SELECT"].includes(document.activeElement.tagName)) return;

  if (["ArrowRight", "ArrowDown", " "].includes(event.key)) {
    event.preventDefault();
    nextSlide();
  }
  if (["ArrowLeft", "ArrowUp"].includes(event.key)) {
    event.preventDefault();
    previousSlide();
  }
  if (event.key === "Home") showSlide(0);
  if (event.key === "End") showSlide(slideCount - 1);
  if (event.key.toLowerCase() === "n") notesButton.click();
});

showSlide(0);
