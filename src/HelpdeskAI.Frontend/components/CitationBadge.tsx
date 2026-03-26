"use client";

import React from "react";

/**
 * Inline citation badge for KB article references.
 * Renders [KB-xxx] as a styled purple pill in the chat.
 */
export function CitationBadge({ id }: { id: string }) {
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 3,
        fontSize: 10,
        fontWeight: 600,
        fontFamily: "monospace",
        color: "#6366f1",
        background: "#6366f118",
        padding: "1px 7px",
        borderRadius: 4,
        verticalAlign: "middle",
        cursor: "default",
        letterSpacing: "0.02em",
      }}
      title={`Knowledge Base article: ${id}`}
    >
      📖 {id}
    </span>
  );
}

/**
 * Regex for matching KB citation patterns like [KB-up-abc123] in agent text.
 */
export const KB_CITATION_REGEX = /\[(KB-[a-zA-Z0-9_-]+)\]/g;

/**
 * Splits a markdown string into text segments and CitationBadge components.
 * Used by the custom AssistantMessage to render inline citations.
 */
export function renderWithCitations(text: string): React.ReactNode[] {
  const parts: React.ReactNode[] = [];
  let lastIndex = 0;
  let match: RegExpExecArray | null;
  const regex = new RegExp(KB_CITATION_REGEX.source, "g");

  while ((match = regex.exec(text)) !== null) {
    if (match.index > lastIndex) {
      parts.push(text.slice(lastIndex, match.index));
    }
    parts.push(<CitationBadge key={`cite-${match.index}`} id={match[1]} />);
    lastIndex = regex.lastIndex;
  }

  if (lastIndex < text.length) {
    parts.push(text.slice(lastIndex));
  }

  return parts;
}
