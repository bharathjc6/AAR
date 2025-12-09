import { describe, it, expect } from 'vitest';
import { render, screen } from '../test/test-utils';
import { StatusBadge } from './StatusBadge';

describe('StatusBadge', () => {
  it('renders pending status', () => {
    render(<StatusBadge status="pending" />);
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });

  it('renders analyzing status', () => {
    render(<StatusBadge status="analyzing" />);
    expect(screen.getByText('Analyzing')).toBeInTheDocument();
  });

  it('renders completed status', () => {
    render(<StatusBadge status="completed" />);
    expect(screen.getByText('Completed')).toBeInTheDocument();
  });

  it('renders failed status', () => {
    render(<StatusBadge status="failed" />);
    expect(screen.getByText('Failed')).toBeInTheDocument();
  });

  it('renders filesReady status', () => {
    render(<StatusBadge status="filesReady" />);
    expect(screen.getByText('Files Ready')).toBeInTheDocument();
  });

  it('handles numeric status values', () => {
    render(<StatusBadge status={5} />);
    expect(screen.getByText('Completed')).toBeInTheDocument();
  });
});
