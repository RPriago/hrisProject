import { useEffect, useState, type RefObject } from 'react';

/**
 * Observes a container's CSS pixel width so SVG charts render in 1:1 coordinate space
 * without `preserveAspectRatio="none"` distortion on strokes or circular markers.
 */
export function useChartWidth(
  containerRef: RefObject<HTMLElement | null>,
  fallbackWidth: number = 520
): number {
  const [width, setWidth] = useState<number>(fallbackWidth);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;

    const measure = () => {
      const measured = element.getBoundingClientRect().width;
      if (measured > 0) {
        setWidth(Math.round(measured));
      }
    };

    measure();

    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', measure);
      return () => window.removeEventListener('resize', measure);
    }

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const nextWidth = Math.round(entry.contentRect.width);
        if (nextWidth > 0) {
          setWidth(nextWidth);
        }
      }
    });

    observer.observe(element);
    return () => observer.disconnect();
  }, [containerRef]);

  return width;
}
