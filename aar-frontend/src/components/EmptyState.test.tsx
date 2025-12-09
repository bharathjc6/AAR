import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '../test/test-utils';
import { EmptyState } from './EmptyState';

describe('EmptyState', () => {
  it('renders with title and description', () => {
    render(
      <EmptyState
        type="empty"
        title="No items"
        description="Get started by creating a new item"
      />
    );
    
    expect(screen.getByText('No items')).toBeInTheDocument();
    expect(screen.getByText('Get started by creating a new item')).toBeInTheDocument();
  });

  it('renders no-results type', () => {
    render(
      <EmptyState
        type="no-results"
        title="No results found"
        description="Try different search terms"
      />
    );
    
    expect(screen.getByText('No results found')).toBeInTheDocument();
  });

  it('renders error type', () => {
    render(
      <EmptyState
        type="error"
        title="Something went wrong"
        description="Please try again"
      />
    );
    
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
  });

  it('renders with action button', () => {
    const onClickMock = vi.fn();
    render(
      <EmptyState
        type="empty"
        title="No items"
        description="Get started"
        action={{ label: 'Create New', onClick: onClickMock }}
      />
    );
    
    expect(screen.getByText('Create New')).toBeInTheDocument();
  });
});
